namespace TubeForge.Core.Settings;

public sealed record TubeForgeSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string DownloadFolder { get; init; }

    public int MaximumConcurrentDownloads { get; init; } = 2;

    public bool ResponsibleUseAccepted { get; init; }
}
