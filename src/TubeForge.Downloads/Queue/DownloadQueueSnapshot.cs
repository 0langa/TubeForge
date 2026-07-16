namespace TubeForge.Downloads.Queue;

public sealed record DownloadQueueSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<DownloadQueueItem> Items { get; init; } = [];
}
