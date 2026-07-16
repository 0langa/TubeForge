using TubeForge.Tests.Framework;
using TubeForge.YouTube.Diagnostics;

namespace TubeForge.Tests.YouTube;

public static class CanaryListParserTests
{
    [Test]
    public static void ParsesCommentsAndStrictUrlsWithoutRetainingUrls()
    {
        var result = CanaryListParser.Parse([
            "# local canaries",
            "",
            " https://www.youtube.com/watch?v=dQw4w9WgXcQ ",
            "https://youtu.be/aqz-KE-bpKQ"
        ]);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.SequenceEqual(new[] { "dQw4w9WgXcQ", "aqz-KE-bpKQ" }, result.Value.Select(id => id.Value));
    }

    [Test]
    public static void RejectsEmptyInvalidDuplicateAndExcessiveLists()
    {
        Assert.Equal("Canary.Empty", CanaryListParser.Parse(["", "# comment"]).Error?.Code);
        Assert.Equal("Canary.InvalidUrl", CanaryListParser.Parse(["https://example.com/watch?v=dQw4w9WgXcQ"]).Error?.Code);
        Assert.Equal("Canary.Duplicate", CanaryListParser.Parse([
            "https://youtu.be/dQw4w9WgXcQ",
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ"
        ]).Error?.Code);
        Assert.Equal(
            "Canary.TooManyItems",
            CanaryListParser.Parse(Enumerable.Range(0, CanaryListParser.MaximumCanaries + 1)
                .Select(index => $"https://youtu.be/A{index:D10}"))
                .Error?.Code);
    }
}
