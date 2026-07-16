namespace TubeForge.Core.Media;

public enum DownloadMode
{
    AudioVideo,
    AudioOnly,
    VideoOnly
}

public sealed record FormatSelectionCriteria
{
    public DownloadMode Mode { get; init; } = DownloadMode.AudioVideo;

    public int? Height { get; init; }

    public MediaContainer? Container { get; init; }

    public VideoCodec? VideoCodec { get; init; }

    public AudioCodec? AudioCodec { get; init; }

    public int? FramesPerSecond { get; init; }

    public long? Bitrate { get; init; }

    public bool? IsHdr { get; init; }
}

public static class FormatFilter
{
    public static IReadOnlyList<StreamFormat> Apply(
        IEnumerable<StreamFormat> formats,
        FormatSelectionCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(formats);
        ArgumentNullException.ThrowIfNull(criteria);

        var kind = criteria.Mode switch
        {
            DownloadMode.AudioVideo => StreamKind.Progressive,
            DownloadMode.AudioOnly => StreamKind.AudioOnly,
            DownloadMode.VideoOnly => StreamKind.VideoOnly,
            _ => throw new ArgumentOutOfRangeException(nameof(criteria), criteria.Mode, "Unknown download mode.")
        };

        return FormatRanker.RankForDownload(formats.Where(format =>
            format.Kind == kind &&
            (criteria.Height is null || format.Height == criteria.Height) &&
            (criteria.Container is null || format.Container == criteria.Container) &&
            (criteria.VideoCodec is null || format.VideoCodec == criteria.VideoCodec) &&
            (criteria.AudioCodec is null || format.AudioCodec == criteria.AudioCodec) &&
            (criteria.FramesPerSecond is null || format.FramesPerSecond == criteria.FramesPerSecond) &&
            (criteria.Bitrate is null || format.Bitrate == criteria.Bitrate) &&
            (criteria.IsHdr is null || format.IsHdr == criteria.IsHdr)));
    }
}
