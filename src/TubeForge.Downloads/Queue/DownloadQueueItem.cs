namespace TubeForge.Downloads.Queue;

public sealed record DownloadQueueItem
{
    public required Guid Id { get; init; }

    public required string VideoId { get; init; }

    public required int FormatId { get; init; }

    public required string SourceIdentity { get; init; }

    public required string DisplayTitle { get; init; }

    public required string DestinationPath { get; init; }

    public long? ExpectedLength { get; init; }

    public long BytesReceived { get; init; }

    public DownloadQueueStatus Status { get; init; }

    public required DateTimeOffset CreatedAtUtc { get; init; }

    public required DateTimeOffset UpdatedAtUtc { get; init; }

    public string? FailureCode { get; init; }
}
