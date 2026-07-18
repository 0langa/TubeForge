namespace TubeForge.Downloads.Queue;

public sealed record DownloadQueueSnapshot
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<DownloadQueueItem> Items { get; init; } = [];
}
