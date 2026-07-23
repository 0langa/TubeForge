using TubeForge.Core.YouTube;

namespace TubeForge.Downloads.Archives;

public enum ArchiveOutputPreset
{
    BestOriginal,
    WindowsCompatibleMp4,
    SmallFile,
    Mp3_320
}

public enum ArchiveCaptionPreference
{
    None,
    ManualPreferred,
    ManualOrAutomatic
}

public sealed record CollectionArchiveProfile
{
    public required Guid Id { get; init; }

    public required YouTubeCollectionKind SourceKind { get; init; }

    public required string SourceUrl { get; init; }

    public required string DisplayName { get; init; }

    public required string DestinationPath { get; init; }

    public required string FileNameTemplate { get; init; }

    public ArchiveOutputPreset OutputPreset { get; init; }

    public ArchiveCaptionPreference CaptionPreference { get; init; }

    public bool EmbedChapters { get; init; }

    public IReadOnlyList<string> LastCheckedVideoIds { get; init; } = [];

    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset LastCheckedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record CollectionArchiveSnapshot
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<CollectionArchiveProfile> Profiles { get; init; } = [];
}
