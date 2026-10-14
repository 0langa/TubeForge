using TubeForge.Core.Files;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class FileNamePolicyTests
{
    [Test]
    public static void SanitizesWindowsUnsafeNames()
    {
        Assert.Equal("A title with bad chars", FileNamePolicy.SanitizeStem(" A  title: with / bad*chars... "));
        Assert.Equal("_CON", FileNamePolicy.SanitizeStem("CON"));
        Assert.Equal("video", FileNamePolicy.SanitizeStem("<>:\"/\\|?*"));
        Assert.Equal("hello world", FileNamePolicy.SanitizeStem("hello\t\r\nworld"));
    }

    [Test]
    public static void PrefixesEveryLegacyDosDeviceName()
    {
        // Windows 10 still opens a device for these names even with an extension appended. It
        // matches only the text before the first dot, ignores spaces before that dot, and treats
        // superscript 1-3 as COM/LPT digits.
        string[] deviceNames =
        [
            "COM0", "LPT0", "COM¹", "com²", "LPT³", "CONIN$", "conout$",
            "NUL . part two", "aux .final", "Com1 .x", "PRN.tar"
        ];
        foreach (var name in deviceNames)
        {
            Assert.True(
                FileNamePolicy.SanitizeStem(name).StartsWith('_'),
                $"'{name}' must not remain a DOS device name.");
        }

        Assert.Equal("Console", FileNamePolicy.SanitizeStem("Console"));
        Assert.Equal("COM10", FileNamePolicy.SanitizeStem("COM10"));
        Assert.Equal("NULL", FileNamePolicy.SanitizeStem("NULL"));
        Assert.Equal("LPT¹ notes", FileNamePolicy.SanitizeStem("LPT¹ notes"));
    }

    [Test]
    public static void DoesNotSplitUnicodeSurrogatePairsAtLimit()
    {
        Assert.Equal("ab😀", FileNamePolicy.SanitizeStem("ab😀z", 4));
        Assert.Equal("ab", FileNamePolicy.SanitizeStem("ab😀", 3));
    }

    [Test]
    public static void ProducesDirectChildAndAddsCollisionSuffix()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "tubeforge-tests"));
        var first = Path.Combine(root, "title.mp4");
        var second = Path.Combine(root, "title (1).mp4");
        var actual = FileNamePolicy.AvailablePath(root, "..\\title", "mp4", path =>
            path.Equals(first, StringComparison.OrdinalIgnoreCase));

        Assert.Equal(second, actual);
    }

    [Test]
    public static void RejectsInvalidExtension()
    {
        Assert.Throws<ArgumentException>(() =>
            FileNamePolicy.AvailablePath(Path.GetTempPath(), "title", "../exe", _ => false));
    }
}
