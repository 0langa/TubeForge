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
