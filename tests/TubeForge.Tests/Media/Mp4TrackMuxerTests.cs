using System.Buffers.Binary;
using System.Text;
using TubeForge.Media.IsoBmff;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Media;

public static class Mp4TrackMuxerTests
{
    [Test]
    public static async Task MuxesVideoAndAudioTracksWithRewrittenChunkOffsets()
    {
        using var directory = new MediaTestDirectory();
        var videoPath = Path.Combine(directory.Path, "video.mp4");
        var audioPath = Path.Combine(directory.Path, "audio.m4a");
        var outputPath = Path.Combine(directory.Path, "combined.mp4");
        await File.WriteAllBytesAsync(videoPath, SyntheticMp4.Track("vide", "VIDEO-SAMPLES"u8, 1, 90_000, 450_000));
        await File.WriteAllBytesAsync(audioPath, SyntheticMp4.Track("soun", "AUDIO-SAMPLES"u8, 1, 48_000, 240_000));

        var result = await Mp4TrackMuxer.MuxAsync(videoPath, audioPath, outputPath);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(File.Exists(outputPath));
        Assert.False(File.Exists(outputPath + ".muxing"));
        var boxes = IsoBmffReader.ReadTopLevel(outputPath);
        Assert.True(boxes.IsSuccess, boxes.Error?.Message);
        Assert.SequenceEqual(new[] { "ftyp", "moov", "mdat", "mdat" }, boxes.Value.Select(box => box.Type));

        var output = await File.ReadAllBytesAsync(outputPath);
        Assert.SequenceEqual(new[] { "vide", "soun" }, FindHandlerTypes(output));
        var offsets = FindChunkOffsets(output);
        Assert.Equal(2, offsets.Count);
        Assert.True(output.AsSpan(checked((int)offsets[0])).StartsWith("VIDEO-SAMPLES"u8));
        Assert.True(output.AsSpan(checked((int)offsets[1])).StartsWith("AUDIO-SAMPLES"u8));
    }

    [Test]
    public static async Task RejectsWrongTrackKindsWithoutPublishingOutput()
    {
        using var directory = new MediaTestDirectory();
        var videoPath = Path.Combine(directory.Path, "not-video.mp4");
        var audioPath = Path.Combine(directory.Path, "audio.m4a");
        var outputPath = Path.Combine(directory.Path, "combined.mp4");
        await File.WriteAllBytesAsync(videoPath, SyntheticMp4.Track("soun", "WRONG"u8, 1, 48_000, 1000));
        await File.WriteAllBytesAsync(audioPath, SyntheticMp4.Track("soun", "AUDIO"u8, 1, 48_000, 1000));

        var result = await Mp4TrackMuxer.MuxAsync(videoPath, audioPath, outputPath);

        Assert.False(result.IsSuccess);
        Assert.Equal("Media.IncompatibleTracks", result.Error?.Code);
        Assert.False(File.Exists(outputPath));
    }

    [Test]
    public static async Task MuxesFragmentedTracksInDecodeOrderWithUniqueSequencesAndTrackIds()
    {
        using var directory = new MediaTestDirectory();
        var videoPath = Path.Combine(directory.Path, "video-fragmented.mp4");
        var audioPath = Path.Combine(directory.Path, "audio-fragmented.m4a");
        var outputPath = Path.Combine(directory.Path, "combined-fragmented.mp4");
        await File.WriteAllBytesAsync(videoPath, SyntheticMp4.FragmentedTrack(
            "vide", 1, 1000, (0, "VIDEO-0"u8.ToArray()), (1000, "VIDEO-1"u8.ToArray())));
        await File.WriteAllBytesAsync(audioPath, SyntheticMp4.FragmentedTrack(
            "soun", 1, 1000, (0, "AUDIO-0"u8.ToArray()), (1000, "AUDIO-1"u8.ToArray())));

        var result = await Mp4TrackMuxer.MuxAsync(videoPath, audioPath, outputPath);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var boxes = IsoBmffReader.ReadTopLevel(outputPath);
        Assert.True(boxes.IsSuccess, boxes.Error?.Message);
        Assert.SequenceEqual(
            new[] { "ftyp", "moov", "moof", "mdat", "moof", "mdat", "moof", "mdat", "moof", "mdat" },
            boxes.Value.Select(box => box.Type));
        var output = await File.ReadAllBytesAsync(outputPath);
        Assert.SequenceEqual(new uint[] { 1, 2, 1, 2 }, FindFullBoxUInt32(output, "tfhd"));
        Assert.SequenceEqual(new uint[] { 1, 2, 3, 4 }, FindFullBoxUInt32(output, "mfhd"));
    }

    private static IReadOnlyList<string> FindHandlerTypes(byte[] bytes)
    {
        var handlers = new List<string>();
        for (var index = 4; index <= bytes.Length - 16; index++)
        {
            if (bytes.AsSpan(index, 4).SequenceEqual("hdlr"u8))
            {
                handlers.Add(Encoding.ASCII.GetString(bytes, index + 12, 4));
            }
        }

        return handlers;
    }

    private static IReadOnlyList<long> FindChunkOffsets(byte[] bytes)
    {
        var offsets = new List<long>();
        for (var index = 4; index <= bytes.Length - 20; index++)
        {
            if (!bytes.AsSpan(index, 4).SequenceEqual("co64"u8))
            {
                continue;
            }

            var entryCount = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index + 8, 4));
            for (var entry = 0; entry < entryCount; entry++)
            {
                offsets.Add(checked((long)BinaryPrimitives.ReadUInt64BigEndian(
                    bytes.AsSpan(index + 12 + (entry * 8), 8))));
            }
        }

        return offsets;
    }

    private static IReadOnlyList<uint> FindFullBoxUInt32(byte[] bytes, string type)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var values = new List<uint>();
        for (var index = 4; index <= bytes.Length - 12; index++)
        {
            if (bytes.AsSpan(index, 4).SequenceEqual(typeBytes))
            {
                values.Add(BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(index + 8, 4)));
            }
        }

        return values;
    }
}

internal static class SyntheticMp4
{
    public static byte[] Track(
        string handlerType,
        ReadOnlySpan<byte> samples,
        uint trackId,
        uint timescale,
        uint duration)
    {
        var ftyp = Box("ftyp", [.. "isom"u8, 0, 0, 0, 0, .. "isom"u8, .. "mp42"u8]);
        var moov = Movie(handlerType, trackId, timescale, duration, 0);
        var chunkOffset = checked((uint)(ftyp.Length + moov.Length + 8));
        moov = Movie(handlerType, trackId, timescale, duration, chunkOffset);
        return [.. ftyp, .. moov, .. Box("mdat", samples)];
    }

    public static byte[] FragmentedTrack(
        string handlerType,
        uint trackId,
        uint timescale,
        params (uint DecodeTime, byte[] Samples)[] fragments)
    {
        var ftyp = Box("ftyp", [.. "isom"u8, 0, 0, 0, 0, .. "isom"u8, .. "mp42"u8]);
        var moov = FragmentedMovie(handlerType, trackId, timescale);
        var fragmentBytes = fragments
            .Select((fragment, index) => Fragment(trackId, (uint)index + 1, fragment.DecodeTime, fragment.Samples))
            .SelectMany(value => value)
            .SelectMany(value => value)
            .ToArray();
        return [.. ftyp, .. moov, .. fragmentBytes];
    }

    private static byte[] Movie(
        string handlerType,
        uint trackId,
        uint timescale,
        uint duration,
        uint chunkOffset)
    {
        var movieHeader = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(movieHeader.AsSpan(12, 4), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(movieHeader.AsSpan(16, 4), duration);
        BinaryPrimitives.WriteUInt32BigEndian(movieHeader.AsSpan(20, 4), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(movieHeader.AsSpan(24, 2), 0x0100);
        BinaryPrimitives.WriteUInt32BigEndian(movieHeader.AsSpan(96, 4), trackId + 1);

        var trackHeader = new byte[84];
        trackHeader[3] = 7;
        BinaryPrimitives.WriteUInt32BigEndian(trackHeader.AsSpan(12, 4), trackId);
        BinaryPrimitives.WriteUInt32BigEndian(trackHeader.AsSpan(20, 4), duration);
        if (handlerType == "soun")
        {
            BinaryPrimitives.WriteUInt16BigEndian(trackHeader.AsSpan(36, 2), 0x0100);
        }

        var mediaHeader = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(mediaHeader.AsSpan(12, 4), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(mediaHeader.AsSpan(16, 4), duration);
        var handler = new byte[25];
        Encoding.ASCII.GetBytes(handlerType, handler.AsSpan(8, 4));
        var chunkOffsets = new byte[12];
        BinaryPrimitives.WriteUInt32BigEndian(chunkOffsets.AsSpan(4, 4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(chunkOffsets.AsSpan(8, 4), chunkOffset);

        var sampleTable = Box("stbl", Box("stco", chunkOffsets));
        var media = Box("mdia", [
            .. Box("mdhd", mediaHeader),
            .. Box("hdlr", handler),
            .. Box("minf", sampleTable)
        ]);
        var track = Box("trak", [.. Box("tkhd", trackHeader), .. media]);
        return Box("moov", [.. Box("mvhd", movieHeader), .. track]);
    }

    private static byte[] FragmentedMovie(string handlerType, uint trackId, uint timescale)
    {
        var movieHeader = new byte[100];
        BinaryPrimitives.WriteUInt32BigEndian(movieHeader.AsSpan(12, 4), timescale);
        BinaryPrimitives.WriteUInt32BigEndian(movieHeader.AsSpan(20, 4), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(movieHeader.AsSpan(24, 2), 0x0100);
        BinaryPrimitives.WriteUInt32BigEndian(movieHeader.AsSpan(96, 4), trackId + 1);
        var trackHeader = new byte[84];
        trackHeader[3] = 7;
        BinaryPrimitives.WriteUInt32BigEndian(trackHeader.AsSpan(12, 4), trackId);
        var mediaHeader = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(mediaHeader.AsSpan(12, 4), timescale);
        var handler = new byte[25];
        Encoding.ASCII.GetBytes(handlerType, handler.AsSpan(8, 4));
        var emptyChunkOffsets = new byte[8];
        var sampleTable = Box("stbl", Box("stco", emptyChunkOffsets));
        var media = Box("mdia", [
            .. Box("mdhd", mediaHeader),
            .. Box("hdlr", handler),
            .. Box("minf", sampleTable)
        ]);
        var track = Box("trak", [.. Box("tkhd", trackHeader), .. media]);
        var trackExtends = new byte[24];
        BinaryPrimitives.WriteUInt32BigEndian(trackExtends.AsSpan(4, 4), trackId);
        var movieExtends = Box("mvex", Box("trex", trackExtends));
        return Box("moov", [.. Box("mvhd", movieHeader), .. track, .. movieExtends]);
    }

    private static byte[][] Fragment(uint trackId, uint sequence, uint decodeTime, byte[] samples)
    {
        var movieFragmentHeader = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(movieFragmentHeader.AsSpan(4, 4), sequence);
        var trackFragmentHeader = new byte[8];
        trackFragmentHeader[1] = 0x02;
        BinaryPrimitives.WriteUInt32BigEndian(trackFragmentHeader.AsSpan(4, 4), trackId);
        var decodeTimeBox = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(decodeTimeBox.AsSpan(4, 4), decodeTime);
        var run = new byte[12];
        run[3] = 1;
        BinaryPrimitives.WriteUInt32BigEndian(run.AsSpan(4, 4), 1);
        var placeholderMoof = Box("moof", [
            .. Box("mfhd", movieFragmentHeader),
            .. Box("traf", [
                .. Box("tfhd", trackFragmentHeader),
                .. Box("tfdt", decodeTimeBox),
                .. Box("trun", run)
            ])
        ]);
        BinaryPrimitives.WriteInt32BigEndian(run.AsSpan(8, 4), placeholderMoof.Length + 8);
        var moof = Box("moof", [
            .. Box("mfhd", movieFragmentHeader),
            .. Box("traf", [
                .. Box("tfhd", trackFragmentHeader),
                .. Box("tfdt", decodeTimeBox),
                .. Box("trun", run)
            ])
        ]);
        return [moof, Box("mdat", samples)];
    }

    private static byte[] Box(string type, ReadOnlySpan<byte> payload)
    {
        var bytes = new byte[checked(payload.Length + 8)];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)bytes.Length));
        Encoding.ASCII.GetBytes(type, bytes.AsSpan(4, 4));
        payload.CopyTo(bytes.AsSpan(8));
        return bytes;
    }
}
