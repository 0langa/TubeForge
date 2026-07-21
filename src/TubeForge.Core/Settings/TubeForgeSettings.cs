namespace TubeForge.Core.Settings;

public sealed record TubeForgeSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string DownloadFolder { get; init; }

    public int MaximumConcurrentDownloads { get; init; } = 2;

    public string FileNameTemplate { get; init; } = Files.FileNameTemplate.Default;

    public bool EnableAcceleratedTransfers { get; init; } = true;

    public bool EnableAutomaticUpdateChecks { get; init; } = true;

    public LibrarySortOrder LibrarySortOrder { get; init; } = LibrarySortOrder.NewestFirst;

    public bool ResponsibleUseAccepted { get; init; }
}

public enum LibrarySortOrder
{
    NewestFirst,
    OldestFirst,
    TitleAscending,
    LargestFirst
}
