namespace TubeForge.Core.Settings;

public sealed record TubeForgeSettings
{
    public const int CurrentSchemaVersion = 5;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string DownloadFolder { get; init; }

    public int MaximumConcurrentDownloads { get; init; } = 2;

    public string FileNameTemplate { get; init; } = Files.FileNameTemplate.Default;

    public bool EnableAcceleratedTransfers { get; init; } = true;

    public bool EnableAutomaticUpdateChecks { get; init; } = true;

    public NetworkProxyMode ProxyMode { get; init; } = NetworkProxyMode.System;

    public string ManualProxyUri { get; init; } = string.Empty;

    public int MetadataTimeoutSeconds { get; init; } = 20;

    public int DownloadRetryAttempts { get; init; } = 3;

    public int PerHostConcurrency { get; init; } = 2;

    public LibrarySortOrder LibrarySortOrder { get; init; } = LibrarySortOrder.NewestFirst;

    public PreferredDownloadPreset DefaultDownloadPreset { get; init; } = PreferredDownloadPreset.BestOriginal;

    public bool ShowAdvancedDownloadOptions { get; init; }

    public bool ResponsibleUseAccepted { get; init; }
}

public enum NetworkProxyMode
{
    System,
    Manual,
    None
}

public enum LibrarySortOrder
{
    NewestFirst,
    OldestFirst,
    TitleAscending,
    LargestFirst
}

public enum PreferredDownloadPreset
{
    BestOriginal,
    WindowsCompatibleMp4,
    SmallFile,
    Mp3_320
}
