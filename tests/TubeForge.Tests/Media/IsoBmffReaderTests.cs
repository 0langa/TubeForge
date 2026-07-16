using System.Buffers.Binary;
using TubeForge.Media.IsoBmff;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Media;

public static class IsoBmffReaderTests
{
    [Test]
    public static async Task ReadsStandardExtendedAndEndOfFileBoxes()
    {
        using var directory = new MediaTestDirectory();
        var path = Path.Combine(directory.Path, "boxes.mp4");
        var bytes = new byte[40];
        WriteBoxHeader(bytes.AsSpan(0, 8), 8, "free");
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 1);
        "mdat"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(16, 8), 24);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(32, 4), 0);
        "skip"u8.CopyTo(bytes.AsSpan(36, 4));
        await File.WriteAllBytesAsync(path, bytes);

        var result = IsoBmffReader.ReadTopLevel(path);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.SequenceEqual(new[] { "free", "mdat", "skip" }, result.Value.Select(box => box.Type));
        Assert.SequenceEqual(new long[] { 8, 24, 8 }, result.Value.Select(box => box.Size));
        Assert.SequenceEqual(new[] { 8, 16, 8 }, result.Value.Select(box => box.HeaderSize));
    }

    [Test]
    public static async Task RejectsTruncatedAndOversizedBoxes()
    {
        using var directory = new MediaTestDirectory();
        var truncated = Path.Combine(directory.Path, "truncated.mp4");
        var oversized = Path.Combine(directory.Path, "oversized.mp4");
        await File.WriteAllBytesAsync(truncated, [0x00, 0x00, 0x00, 0x08, 0x66]);
        var bytes = new byte[8];
        WriteBoxHeader(bytes, 1024, "mdat");
        await File.WriteAllBytesAsync(oversized, bytes);

        var truncatedResult = IsoBmffReader.ReadTopLevel(truncated);
        var oversizedResult = IsoBmffReader.ReadTopLevel(oversized);

        Assert.False(truncatedResult.IsSuccess);
        Assert.Equal("Media.InvalidIsoBmff", truncatedResult.Error?.Code);
        Assert.False(oversizedResult.IsSuccess);
        Assert.Equal("Media.InvalidIsoBmff", oversizedResult.Error?.Code);
    }

    private static void WriteBoxHeader(Span<byte> destination, uint size, string type)
    {
        BinaryPrimitives.WriteUInt32BigEndian(destination, size);
        System.Text.Encoding.ASCII.GetBytes(type, destination[4..]);
    }
}

internal sealed class MediaTestDirectory : IDisposable
{
    private static readonly string SafeRoot = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests"));

    public MediaTestDirectory()
    {
        Path = System.IO.Path.Combine(SafeRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        var resolved = System.IO.Path.GetFullPath(Path);
        if (!resolved.StartsWith(SafeRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to clean a test directory outside the safe root.");
        }

        if (Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }
}
