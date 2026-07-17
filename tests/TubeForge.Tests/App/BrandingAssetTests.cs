using System.Buffers.Binary;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.App;

public static class BrandingAssetTests
{
    [Test]
    public static void WindowsIconContainsExpectedPngSizes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TubeForge.ico");
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 6 + (9 * 16));
        Assert.Equal((ushort)0, BinaryPrimitives.ReadUInt16LittleEndian(bytes));
        Assert.Equal((ushort)1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        Assert.Equal((ushort)9, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)));

        for (var index = 0; index < 9; index++)
        {
            var entry = 6 + (index * 16);
            var length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 8));
            var offset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(entry + 12));
            Assert.True(length > 8 && offset >= 6 + (9 * 16) && offset + length <= bytes.Length);
            Assert.SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a },
                bytes.AsSpan(offset, 8).ToArray());
        }
    }

    [Test]
    public static void VectorMarkCarriesTubeForgeAccessibleIdentity()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "TubeForge.svg");
        var svg = File.ReadAllText(path);
        Assert.True(svg.Contains("<title id=\"title\">TubeForge</title>", StringComparison.Ordinal));
        Assert.True(svg.Contains("#ffad42", StringComparison.Ordinal));
        Assert.True(svg.Contains("#5c3ee6", StringComparison.Ordinal));
    }
}
