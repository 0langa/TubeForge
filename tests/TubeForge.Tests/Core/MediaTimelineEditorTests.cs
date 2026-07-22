using TubeForge.Core.Media;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class MediaTimelineEditorTests
{
    [Test]
    public static void KeepsIntersectingChaptersAndRebasesTheirStarts()
    {
        IReadOnlyList<VideoChapter> chapters =
        [
            new VideoChapter { Title = "Intro", StartTime = TimeSpan.Zero },
            new VideoChapter { Title = "Main", StartTime = TimeSpan.FromSeconds(30) },
            new VideoChapter { Title = "Outro", StartTime = TimeSpan.FromSeconds(90) }
        ];
        Assert.True(MediaTrimRange.TryCreate(
            TimeSpan.FromSeconds(20),
            TimeSpan.FromSeconds(100),
            out var trim));

        var adjusted = MediaTimelineEditor.TrimChapters(
            chapters,
            TimeSpan.FromMinutes(2),
            trim);

        Assert.Equal(3, adjusted.Count);
        Assert.Equal("Intro", adjusted[0].Title);
        Assert.Equal(TimeSpan.Zero, adjusted[0].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(10), adjusted[1].StartTime);
        Assert.Equal(TimeSpan.FromSeconds(70), adjusted[2].StartTime);
    }

    [Test]
    public static void NormalizesMergesAndMarksSponsorSegments()
    {
        IReadOnlyList<SponsorBlockSegment> segments =
        [
            new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), "intro", string.Empty),
            new(TimeSpan.FromSeconds(14), TimeSpan.FromSeconds(20), "sponsor", string.Empty),
            new(TimeSpan.FromSeconds(70), TimeSpan.FromSeconds(90), "outro", string.Empty)
        ];
        var trim = new MediaTrimRange(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(80));

        var normalized = MediaTimelineEditor.NormalizeSponsorSegments(
            segments,
            TimeSpan.FromSeconds(100),
            trim);
        var merged = MediaTimelineEditor.MergeRemovalRanges(normalized);
        var chapters = MediaTimelineEditor.AddSponsorBlockChapters(
            [new VideoChapter { Title = "Original", StartTime = TimeSpan.Zero }],
            normalized,
            trim.Duration);

        Assert.Equal(3, normalized.Count);
        Assert.Equal(TimeSpan.Zero, normalized[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(10), normalized[1].End);
        Assert.Equal(2, merged.Count);
        Assert.Equal(new MediaTrimRange(TimeSpan.Zero, TimeSpan.FromSeconds(10)), merged[0]);
        Assert.True(chapters.Any(chapter => chapter.Title == "SponsorBlock: Intro"));
        Assert.True(chapters.Any(chapter => chapter.Title == "SponsorBlock: Outro"));
    }
}
