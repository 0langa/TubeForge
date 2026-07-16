using TubeForge.Core.Media;
using TubeForge.Media;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Media;

public static class MediaContainerValidatorTests
{
    [Test]
    public static async Task AcceptsBoundedMp4AndWebMHeaders()
    {
        using var directory = new TestDirectory();
        var mp4Path = Path.Combine(directory.Path, "fixture.m4a");
        var webMPath = Path.Combine(directory.Path, "fixture.webm");
        await File.WriteAllBytesAsync(mp4Path,
        [
            0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70,
            0x4D, 0x34, 0x41, 0x20, 0x00, 0x00, 0x00, 0x00,
            0x69, 0x73, 0x6F, 0x6D, 0x6D, 0x70, 0x34, 0x32
        ]);
        await File.WriteAllBytesAsync(webMPath,
        [
            0x1A, 0x45, 0xDF, 0xA3, 0x84, 0x42, 0x86, 0x81, 0x01
        ]);

        Assert.True(MediaContainerValidator.Validate(mp4Path, MediaContainer.Mp4).IsSuccess);
        Assert.True(MediaContainerValidator.Validate(mp4Path, MediaContainer.ThreeGp).IsSuccess);
        Assert.True(MediaContainerValidator.Validate(webMPath, MediaContainer.WebM).IsSuccess);
    }

    [Test]
    public static async Task RejectsWrongMagicTruncationAndOversizedBoxes()
    {
        using var directory = new TestDirectory();
        var wrongMagic = Path.Combine(directory.Path, "wrong.webm");
        var truncated = Path.Combine(directory.Path, "truncated.mp4");
        var oversized = Path.Combine(directory.Path, "oversized.mp4");
        await File.WriteAllBytesAsync(wrongMagic, [0x00, 0x45, 0xDF, 0xA3, 0x80]);
        await File.WriteAllBytesAsync(truncated, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);
        await File.WriteAllBytesAsync(oversized,
        [
            0x7F, 0xFF, 0xFF, 0xFF, 0x66, 0x74, 0x79, 0x70,
            0x69, 0x73, 0x6F, 0x6D, 0x00, 0x00, 0x00, 0x00
        ]);

        Assert.False(MediaContainerValidator.Validate(wrongMagic, MediaContainer.WebM).IsSuccess);
        Assert.False(MediaContainerValidator.Validate(truncated, MediaContainer.Mp4).IsSuccess);
        Assert.False(MediaContainerValidator.Validate(oversized, MediaContainer.Mp4).IsSuccess);
    }

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests"));

        public TestDirectory()
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
}
