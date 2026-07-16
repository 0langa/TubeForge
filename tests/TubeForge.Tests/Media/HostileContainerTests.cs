using System.Buffers.Binary;
using TubeForge.Media.Ebml;
using TubeForge.Media.IsoBmff;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Media;

public static class HostileContainerTests
{
    [Test]
    public static async Task IsoBmffReaderFailsClosedAcrossHostileSizesAndDeterministicMutations()
    {
        using var directory = new MediaTestDirectory();
        var path = Path.Combine(directory.Path, "hostile.mp4");
        var hostileHeaders = new[]
        {
            Bytes(0, 0, 0, 7, "free"u8.ToArray()),
            Bytes(0, 0, 0, 1, "mdat"u8.ToArray(), 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF),
            Bytes(0, 0, 0, 16, "uuid"u8.ToArray(), 0, 0, 0, 0, 0, 0, 0, 0),
            Bytes(0, 0, 0, 8, "free"u8.ToArray(), 0x00),
            Bytes(0, 0, 4, 0, "mdat"u8.ToArray())
        };

        foreach (var bytes in hostileHeaders)
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = IsoBmffReader.ReadTopLevel(path);
            Assert.False(result.IsSuccess);
            Assert.Equal("Media.InvalidIsoBmff", result.Error?.Code);
        }

        var valid = SyntheticMp4.Track("vide", "SAMPLES"u8, 1, 90_000, 90_000);
        foreach (var mutation in DeterministicMutations(valid, seed: 0x4D5034, count: 128))
        {
            await File.WriteAllBytesAsync(path, mutation);
            var result = IsoBmffReader.ReadTopLevel(path);
            if (!result.IsSuccess)
            {
                Assert.Equal("Media.InvalidIsoBmff", result.Error?.Code);
                continue;
            }

            Assert.Equal((long)mutation.Length, result.Value.Sum(box => box.Size));
            Assert.Equal((long)mutation.Length, result.Value[^1].Offset + result.Value[^1].Size);
        }
    }

    [Test]
    public static async Task Mp4MuxerRejectsTruncationNestedSizeOverflowAndInvalidChunkOffsetsAtomically()
    {
        using var directory = new MediaTestDirectory();
        var videoPath = Path.Combine(directory.Path, "video.mp4");
        var audioPath = Path.Combine(directory.Path, "audio.m4a");
        var validVideo = SyntheticMp4.Track("vide", "VIDEO"u8, 1, 90_000, 90_000);
        await File.WriteAllBytesAsync(audioPath, SyntheticMp4.Track("soun", "AUDIO"u8, 1, 48_000, 48_000));

        var cuts = new[] { 1, 7, 8, 16, validVideo.Length / 2, validVideo.Length - 1 };
        for (var index = 0; index < cuts.Length; index++)
        {
            var output = Path.Combine(directory.Path, $"truncated-{index}.mp4");
            await File.WriteAllBytesAsync(videoPath, validVideo[..cuts[index]]);
            var result = await Mp4TrackMuxer.MuxAsync(videoPath, audioPath, output);
            Assert.False(result.IsSuccess);
            Assert.False(File.Exists(output));
            Assert.False(File.Exists(output + ".muxing"));
        }

        var oversizedNested = validVideo.ToArray();
        var trackTypeOffset = FindType(oversizedNested, "trak");
        BinaryPrimitives.WriteUInt32BigEndian(oversizedNested.AsSpan(trackTypeOffset - 4, 4), uint.MaxValue);
        await File.WriteAllBytesAsync(videoPath, oversizedNested);
        var nestedOutput = Path.Combine(directory.Path, "oversized-nested.mp4");
        var nestedResult = await Mp4TrackMuxer.MuxAsync(videoPath, audioPath, nestedOutput);
        Assert.False(nestedResult.IsSuccess);
        Assert.False(File.Exists(nestedOutput));

        var invalidOffset = validVideo.ToArray();
        var chunkOffsetType = FindType(invalidOffset, "stco");
        BinaryPrimitives.WriteUInt32BigEndian(invalidOffset.AsSpan(chunkOffsetType + 12, 4), uint.MaxValue);
        await File.WriteAllBytesAsync(videoPath, invalidOffset);
        var offsetOutput = Path.Combine(directory.Path, "invalid-offset.mp4");
        var offsetResult = await Mp4TrackMuxer.MuxAsync(videoPath, audioPath, offsetOutput);
        Assert.False(offsetResult.IsSuccess);
        Assert.False(File.Exists(offsetOutput));
    }

    [Test]
    public static async Task EbmlReaderFailsClosedAcrossHostileVintsBoundariesAndDeterministicMutations()
    {
        using var directory = new MediaTestDirectory();
        var path = Path.Combine(directory.Path, "hostile.webm");
        var hostileDocuments = new[]
        {
            Bytes(0x00, 0x80),
            Bytes(0x1A, 0x45, 0xDF, 0xA3, 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF),
            Bytes(0x1A, 0x45, 0xDF, 0xA3, 0x81, 0x00),
            Bytes(0x1A, 0x45, 0xDF, 0xA3, 0x80, 0x18, 0x53, 0x80, 0x67, 0x84, 0xEC, 0x88)
        };

        foreach (var bytes in hostileDocuments)
        {
            await File.WriteAllBytesAsync(path, bytes);
            var result = EbmlReader.ReadDocument(path);
            Assert.False(result.IsSuccess);
            Assert.Equal("Media.InvalidEbml", result.Error?.Code);
        }

        var valid = SyntheticWebM.Track(1, "V_VP9", (0, "VIDEO"u8.ToArray()));
        foreach (var mutation in DeterministicMutations(valid, seed: 0x45424D4C, count: 128))
        {
            await File.WriteAllBytesAsync(path, mutation);
            var result = EbmlReader.ReadDocument(path);
            if (!result.IsSuccess)
            {
                Assert.Equal("Media.InvalidEbml", result.Error?.Code);
                continue;
            }

            Assert.Equal((long)mutation.Length, result.Value.Segment.EndOffset);
            Assert.True(result.Value.SegmentChildren.All(child => child.EndOffset <= result.Value.Segment.EndOffset));
        }
    }

    [Test]
    public static async Task WebMMuxerRejectsTruncatedAndOversizedMetadataAtomically()
    {
        using var directory = new MediaTestDirectory();
        var videoPath = Path.Combine(directory.Path, "video.webm");
        var audioPath = Path.Combine(directory.Path, "audio.webm");
        var validVideo = SyntheticWebM.Track(1, "V_VP9", (0, "VIDEO"u8.ToArray()));
        await File.WriteAllBytesAsync(audioPath, SyntheticWebM.Track(2, "A_OPUS", (0, "AUDIO"u8.ToArray())));

        var cuts = new[] { 1, 4, 5, validVideo.Length / 2, validVideo.Length - 1 };
        for (var index = 0; index < cuts.Length; index++)
        {
            var output = Path.Combine(directory.Path, $"truncated-{index}.webm");
            await File.WriteAllBytesAsync(videoPath, validVideo[..cuts[index]]);
            var result = await WebMTrackMuxer.MuxAsync(videoPath, audioPath, output);
            Assert.False(result.IsSuccess);
            Assert.False(File.Exists(output));
            Assert.False(File.Exists(output + ".muxing"));
        }

        var oversized = validVideo.ToArray();
        var tracksOffset = FindBytes(oversized, [0x16, 0x54, 0xAE, 0x6B]);
        oversized[tracksOffset + 4] = 0x01;
        for (var index = 1; index < 8; index++)
        {
            oversized[tracksOffset + 4 + index] = 0xFE;
        }

        await File.WriteAllBytesAsync(videoPath, oversized);
        var oversizedOutput = Path.Combine(directory.Path, "oversized-metadata.webm");
        var oversizedResult = await WebMTrackMuxer.MuxAsync(videoPath, audioPath, oversizedOutput);
        Assert.False(oversizedResult.IsSuccess);
        Assert.False(File.Exists(oversizedOutput));
    }

    private static IEnumerable<byte[]> DeterministicMutations(byte[] source, int seed, int count)
    {
        var random = new Random(seed);
        for (var mutationIndex = 0; mutationIndex < count; mutationIndex++)
        {
            var mutation = source.ToArray();
            var changes = random.Next(1, 5);
            for (var change = 0; change < changes; change++)
            {
                var offset = random.Next(mutation.Length);
                mutation[offset] ^= (byte)random.Next(1, 256);
            }

            yield return mutation;
        }
    }

    private static int FindType(byte[] bytes, string type) =>
        FindBytes(bytes, System.Text.Encoding.ASCII.GetBytes(type));

    private static int FindBytes(byte[] bytes, byte[] needle)
    {
        for (var index = 0; index <= bytes.Length - needle.Length; index++)
        {
            if (bytes.AsSpan(index, needle.Length).SequenceEqual(needle))
            {
                return index;
            }
        }

        throw new InvalidDataException("Synthetic fixture does not contain requested marker.");
    }

    private static byte[] Bytes(params object[] values)
    {
        var output = new List<byte>();
        foreach (var value in values)
        {
            switch (value)
            {
                case byte byteValue:
                    output.Add(byteValue);
                    break;
                case int intValue when intValue is >= byte.MinValue and <= byte.MaxValue:
                    output.Add((byte)intValue);
                    break;
                case ReadOnlyMemory<byte> memory:
                    output.AddRange(memory.Span.ToArray());
                    break;
                case byte[] bytes:
                    output.AddRange(bytes);
                    break;
                default:
                    throw new ArgumentException("Unsupported synthetic byte value.", nameof(values));
            }
        }

        return output.ToArray();
    }
}
