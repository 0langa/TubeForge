using TubeForge.Core.YouTube;

namespace TubeForge.Core.Media;

public sealed record VideoMetadata
{
    public required YouTubeVideoId Id { get; init; }

    public required string Title { get; init; }

    public string Channel { get; init; } = string.Empty;

    public TimeSpan? Duration { get; init; }

    public Uri? ThumbnailUrl { get; init; }

    public VideoAvailability Availability { get; init; } = VideoAvailability.Available;

    public VideoContentKind ContentKind { get; init; } = VideoContentKind.Standard;

    public DateTimeOffset? LiveStartedAtUtc { get; init; }

    public DateTimeOffset? LiveEndedAtUtc { get; init; }

    public IReadOnlyList<StreamFormat> Formats { get; init; } = [];

    public IReadOnlyList<CaptionTrack> CaptionTracks { get; init; } = [];

    public IReadOnlyList<VideoChapter> Chapters { get; init; } = [];
}
