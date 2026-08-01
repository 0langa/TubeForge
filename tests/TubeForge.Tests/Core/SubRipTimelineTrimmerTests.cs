using TubeForge.Core.Media;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class SubRipTimelineTrimmerTests
{
    [Test]
    public static void ClipsShiftsDropsAndRenumbersCues()
    {
        const string content = """
            1
            00:00:01,000 --> 00:00:04,000
            Before

            2
            00:00:04,000 --> 00:00:07,000
            Overlap start

            3
            00:00:10,000 --> 00:00:20,000
            Overlap end

            4
            00:00:21,000 --> 00:00:22,000
            After

            """;
        Assert.True(MediaTrimRange.TryCreate(
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(15),
            out var trim));

        var result = SubRipTimelineTrimmer.Trim(content, trim);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Contains(
            "1\r\n00:00:00,000 --> 00:00:02,000\r\nOverlap start",
            StringComparison.Ordinal));
        Assert.True(result.Value.Contains(
            "2\r\n00:00:05,000 --> 00:00:10,000\r\nOverlap end",
            StringComparison.Ordinal));
        Assert.False(result.Value.Contains("Before", StringComparison.Ordinal));
        Assert.False(result.Value.Contains("After", StringComparison.Ordinal));
    }

    [Test]
    public static void RejectsMalformedOrInvalidTimeline()
    {
        Assert.True(MediaTrimRange.TryCreate(TimeSpan.Zero, TimeSpan.FromSeconds(1), out var trim));
        Assert.False(SubRipTimelineTrimmer.Trim("1\nnot timing\nCaption", trim).IsSuccess);
        Assert.False(SubRipTimelineTrimmer.Trim("", trim).IsSuccess);
    }
}
