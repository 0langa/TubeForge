using TubeForge.Core.YouTube;

namespace TubeForge.YouTube.Collections;

public sealed record YouTubeCollectionItem
{
    public required YouTubeVideoId VideoId { get; init; }

    public required string Title { get; init; }

    public int? Index { get; init; }

    public TimeSpan? Duration { get; init; }

    public Uri? ThumbnailUrl { get; init; }
}

public sealed record YouTubeContinuationContext(
    string ApiKey,
    string ClientVersion,
    string? VisitorData);

public sealed record YouTubeCollectionPage(
    string Title,
    IReadOnlyList<YouTubeCollectionItem> Items,
    string? ContinuationToken,
    YouTubeContinuationContext? ContinuationContext);

public sealed record YouTubeCollectionResult(
    YouTubeCollectionReference Source,
    string Title,
    IReadOnlyList<YouTubeCollectionItem> Items,
    int PagesRead,
    bool IsTruncated);
