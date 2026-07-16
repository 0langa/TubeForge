using System.Buffers.Binary;
using System.Text;
using TubeForge.Media.Ebml;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Media;

public static class WebMTrackMuxerTests
{
    [Test]
    public static async Task MuxesTracksInterleavesClustersAndBuildsCues()
    {
        using var directory = new MediaTestDirectory();
        var videoPath = Path.Combine(directory.Path, "video.webm");
        var audioPath = Path.Combine(directory.Path, "audio.webm");
        var outputPath = Path.Combine(directory.Path, "combined.webm");
        await File.WriteAllBytesAsync(videoPath, SyntheticWebM.Track(
            trackType: 1,
            codecId: "V_VP9",
            (0, "VIDEO-0"u8.ToArray()),
            (100, "VIDEO-100"u8.ToArray())));
        await File.WriteAllBytesAsync(audioPath, SyntheticWebM.Track(
            trackType: 2,
            codecId: "A_OPUS",
            (0, "AUDIO-0"u8.ToArray()),
            (100, "AUDIO-100"u8.ToArray())));

        var result = await WebMTrackMuxer.MuxAsync(videoPath, audioPath, outputPath);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(File.Exists(outputPath));
        var document = EbmlReader.ReadDocument(outputPath);
        Assert.True(document.IsSuccess, document.Error?.Message);
        Assert.SequenceEqual(
            new uint[] { 0x1549A966, 0x1654AE6B, 0x1F43B675, 0x1F43B675, 0x1F43B675, 0x1F43B675, 0x1C53BB6B },
            document.Value.SegmentChildren.Select(element => element.Id));

        var output = await File.ReadAllBytesAsync(outputPath);
        Assert.SequenceEqual(new ulong[] { 1, 2 }, SyntheticWebM.ReadTrackNumbers(output, document.Value));
        Assert.SequenceEqual(new ulong[] { 1, 2, 1, 2 }, SyntheticWebM.ReadClusterTrackNumbers(output, document.Value));
    }

    [Test]
    public static async Task RejectsIncompatibleWebMTrackKindsAtomically()
    {
        using var directory = new MediaTestDirectory();
        var videoPath = Path.Combine(directory.Path, "not-video.webm");
        var audioPath = Path.Combine(directory.Path, "audio.webm");
        var outputPath = Path.Combine(directory.Path, "combined.webm");
        await File.WriteAllBytesAsync(videoPath, SyntheticWebM.Track(2, "A_OPUS", (0, "WRONG"u8.ToArray())));
        await File.WriteAllBytesAsync(audioPath, SyntheticWebM.Track(2, "A_OPUS", (0, "AUDIO"u8.ToArray())));

        var result = await WebMTrackMuxer.MuxAsync(videoPath, audioPath, outputPath);

        Assert.False(result.IsSuccess);
        Assert.Equal("Media.IncompatibleTracks", result.Error?.Code);
        Assert.False(File.Exists(outputPath));
    }
}

internal static class SyntheticWebM
{
    private const uint SegmentId = 0x18538067;
    private const uint InfoId = 0x1549A966;
    private const uint TracksId = 0x1654AE6B;
    private const uint TrackEntryId = 0xAE;
    private const uint TrackNumberId = 0xD7;
    private const uint TrackUidId = 0x73C5;
    private const uint TrackTypeId = 0x83;
    private const uint CodecId = 0x86;
    private const uint ClusterId = 0x1F43B675;
    private const uint TimecodeId = 0xE7;
    private const uint SimpleBlockId = 0xA3;

    public static byte[] Track(
        ulong trackType,
        string codecId,
        params (ulong Timecode, byte[] Samples)[] clusters)
    {
        var ebml = Element(0x1A45DFA3, Element(0x4286, Unsigned(1)));
        var info = Element(InfoId, Element(0x2AD7B1, Unsigned(1_000_000)));
        var entry = Element(TrackEntryId, Concat(
            Element(TrackNumberId, Unsigned(1)),
            Element(TrackUidId, Unsigned(1)),
            Element(TrackTypeId, Unsigned(trackType)),
            Element(CodecId, Encoding.ASCII.GetBytes(codecId))));
        var tracks = Element(TracksId, entry);
        var clusterBytes = clusters.Select(cluster => Element(ClusterId, Concat(
            Element(TimecodeId, Unsigned(cluster.Timecode)),
            Element(SimpleBlockId, [0x81, 0, 0, trackType == 1 ? (byte)0x80 : (byte)0, .. cluster.Samples]))))
            .ToArray();
        var segmentParts = new[] { info, tracks }.Concat(clusterBytes).ToArray();
        return Concat(ebml, Element(SegmentId, Concat(segmentParts)));
    }

    public static IReadOnlyList<ulong> ReadTrackNumbers(byte[] bytes, EbmlDocument document)
    {
        var tracks = document.SegmentChildren.Single(element => element.Id == TracksId);
        var children = ReadChildren(bytes, tracks);
        return children
            .Where(element => element.Id == TrackEntryId)
            .Select(entry => ReadUnsigned(bytes, ReadChildren(bytes, entry).Single(element => element.Id == TrackNumberId)))
            .ToArray();
    }

    public static IReadOnlyList<ulong> ReadClusterTrackNumbers(byte[] bytes, EbmlDocument document) =>
        document.SegmentChildren
            .Where(element => element.Id == ClusterId)
            .Select(cluster => ReadChildren(bytes, cluster).Single(element => element.Id == SimpleBlockId))
            .Select(block => (ulong)(bytes[checked((int)block.DataOffset)] & 0x7F))
            .ToArray();

    private static IReadOnlyList<EbmlElementHeader> ReadChildren(byte[] bytes, EbmlElementHeader parent)
    {
        var children = new List<EbmlElementHeader>();
        var cursor = checked((int)parent.DataOffset);
        var end = checked((int)(parent.DataOffset + parent.DataSize));
        while (cursor < end)
        {
            var element = ReadElement(bytes, cursor, end);
            children.Add(element);
            cursor = checked((int)element.EndOffset);
        }

        return children;
    }

    private static EbmlElementHeader ReadElement(byte[] bytes, int offset, int end)
    {
        var idWidth = VintWidth(bytes[offset], 4);
        uint id = 0;
        for (var index = 0; index < idWidth; index++) id = (id << 8) | bytes[offset + index];
        var sizeOffset = offset + idWidth;
        var sizeWidth = VintWidth(bytes[sizeOffset], 8);
        var marker = 1 << (8 - sizeWidth);
        ulong size = (ulong)(bytes[sizeOffset] & (marker - 1));
        for (var index = 1; index < sizeWidth; index++) size = (size << 8) | bytes[sizeOffset + index];
        var headerSize = idWidth + sizeWidth;
        if (size > (ulong)(end - offset - headerSize)) throw new InvalidDataException("Synthetic EBML parse overflow.");
        return new EbmlElementHeader(id, offset, headerSize, checked((long)size), false);
    }

    private static ulong ReadUnsigned(byte[] bytes, EbmlElementHeader element)
    {
        ulong value = 0;
        for (var offset = element.DataOffset; offset < element.EndOffset; offset++)
        {
            value = (value << 8) | bytes[checked((int)offset)];
        }

        return value;
    }

    private static byte[] Element(uint id, ReadOnlySpan<byte> payload) =>
        Concat(EncodeId(id), EncodeSize(checked((ulong)payload.Length)), payload.ToArray());

    private static byte[] EncodeId(uint id)
    {
        var width = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
        var bytes = new byte[width];
        for (var index = width - 1; index >= 0; index--)
        {
            bytes[index] = (byte)id;
            id >>= 8;
        }

        return bytes;
    }

    private static byte[] EncodeSize(ulong value)
    {
        for (var width = 1; width <= 8; width++)
        {
            var maximum = (1UL << (7 * width)) - 2;
            if (value > maximum) continue;
            var bytes = new byte[width];
            var remaining = value;
            for (var index = width - 1; index >= 0; index--)
            {
                bytes[index] = (byte)remaining;
                remaining >>= 8;
            }

            bytes[0] |= (byte)(1 << (8 - width));
            return bytes;
        }

        throw new ArgumentOutOfRangeException(nameof(value));
    }

    private static byte[] Unsigned(ulong value)
    {
        var width = 1;
        while (width < 8 && value >= (1UL << (width * 8))) width++;
        var bytes = new byte[width];
        for (var index = width - 1; index >= 0; index--)
        {
            bytes[index] = (byte)value;
            value >>= 8;
        }

        return bytes;
    }

    private static int VintWidth(byte first, int maximum)
    {
        for (var width = 1; width <= maximum; width++)
        {
            if ((first & (0x80 >> (width - 1))) != 0) return width;
        }

        throw new InvalidDataException("Invalid EBML VINT.");
    }

    private static byte[] Concat(params byte[][] values)
    {
        var length = values.Sum(value => value.Length);
        var output = new byte[length];
        var offset = 0;
        foreach (var value in values)
        {
            value.CopyTo(output, offset);
            offset += value.Length;
        }

        return output;
    }
}
