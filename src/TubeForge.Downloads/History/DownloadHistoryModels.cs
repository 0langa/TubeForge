namespace TubeForge.Downloads.History;

public sealed record DownloadHistoryEntry
{
    public required Guid Id { get; init; }

    public required string VideoId { get; init; }

    public required string SourceIdentity { get; init; }

    public required string DisplayTitle { get; init; }

    public required string DestinationPath { get; init; }

    public long BytesWritten { get; init; }

    public required DateTimeOffset CompletedAtUtc { get; init; }
}

public sealed record DownloadHistorySnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<DownloadHistoryEntry> Entries { get; init; } = [];
}
