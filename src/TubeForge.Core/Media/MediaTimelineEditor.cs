namespace TubeForge.Core.Media;

public static class MediaTimelineEditor
{
    public static IReadOnlyList<SponsorBlockSegment> NormalizeSponsorSegments(
        IReadOnlyList<SponsorBlockSegment> segments,
        TimeSpan sourceDuration,
        MediaTrimRange? trim = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var windowStart = trim?.Start ?? TimeSpan.Zero;
        var windowEnd = trim?.End ?? sourceDuration;
        if (sourceDuration <= TimeSpan.Zero || windowStart < TimeSpan.Zero ||
            windowEnd <= windowStart || windowEnd > sourceDuration ||
            segments.Count > 1_000 || segments.Any(segment => !segment.IsValid))
        {
            throw new ArgumentException("The SponsorBlock timeline is invalid.", nameof(segments));
        }

        return segments
            .Where(segment => segment.End > windowStart && segment.Start < windowEnd)
            .Select(segment => segment with
            {
                Start = (segment.Start < windowStart ? windowStart : segment.Start) - windowStart,
                End = (segment.End > windowEnd ? windowEnd : segment.End) - windowStart
            })
            .OrderBy(segment => segment.Start)
            .ThenBy(segment => segment.End)
            .ToArray();
    }

    public static IReadOnlyList<MediaTrimRange> MergeRemovalRanges(
        IReadOnlyList<SponsorBlockSegment> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var merged = new List<MediaTrimRange>();
        foreach (var segment in segments.OrderBy(segment => segment.Start).ThenBy(segment => segment.End))
        {
            if (!segment.IsValid || !MediaTrimRange.TryCreate(segment.Start, segment.End, out var range))
            {
                throw new ArgumentException("A SponsorBlock segment is invalid.", nameof(segments));
            }

            if (merged.Count == 0 || range.Start > merged[^1].End)
            {
                merged.Add(range);
                continue;
            }

            if (range.End > merged[^1].End)
            {
                merged[^1] = new MediaTrimRange(merged[^1].Start, range.End);
            }
        }

        return merged;
    }

    public static IReadOnlyList<VideoChapter> AddSponsorBlockChapters(
        IReadOnlyList<VideoChapter> chapters,
        IReadOnlyList<SponsorBlockSegment> segments,
        TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        ArgumentNullException.ThrowIfNull(segments);
        if (duration <= TimeSpan.Zero || segments.Any(segment => !segment.IsValid || segment.End > duration))
        {
            throw new ArgumentException("The SponsorBlock chapter timeline is invalid.", nameof(segments));
        }

        var boundaries = chapters.Select(chapter => chapter.StartTime)
            .Concat(segments.SelectMany(segment => new[] { segment.Start, segment.End }))
            .Where(value => value >= TimeSpan.Zero && value < duration)
            .Append(TimeSpan.Zero)
            .Distinct()
            .Order()
            .ToArray();
        var result = new List<VideoChapter>(boundaries.Length);
        foreach (var boundary in boundaries)
        {
            var active = segments.FirstOrDefault(segment => segment.Start <= boundary && boundary < segment.End);
            var title = active is not null
                ? "SponsorBlock: " + CategoryLabel(active.Category)
                : chapters.LastOrDefault(chapter => chapter.StartTime <= boundary)?.Title ?? "Content";
            result.Add(new VideoChapter { Title = title, StartTime = boundary });
        }

        return result;
    }

    public static IReadOnlyList<VideoChapter> TrimChapters(
        IReadOnlyList<VideoChapter> chapters,
        TimeSpan sourceDuration,
        MediaTrimRange trim)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        if (!trim.IsValid || sourceDuration <= TimeSpan.Zero || trim.End > sourceDuration)
        {
            throw new ArgumentException("The trim timeline is invalid.", nameof(trim));
        }

        var adjusted = new List<VideoChapter>();
        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            var chapterEnd = index + 1 < chapters.Count
                ? chapters[index + 1].StartTime
                : sourceDuration;
            if (chapterEnd <= trim.Start || chapter.StartTime >= trim.End)
            {
                continue;
            }

            var start = (chapter.StartTime < trim.Start ? trim.Start : chapter.StartTime) - trim.Start;
            if (adjusted.Count == 0 || adjusted[^1].StartTime != start)
            {
                adjusted.Add(new VideoChapter { Title = chapter.Title, StartTime = start });
            }
        }

        return adjusted;
    }

    private static string CategoryLabel(string category) => category switch
    {
        "sponsor" => "Sponsor",
        "intro" => "Intro",
        "outro" => "Outro",
        "selfpromo" => "Self-promotion",
        "interaction" => "Interaction reminder",
        "preview" => "Preview",
        "filler" => "Filler",
        _ => "Segment"
    };
}
