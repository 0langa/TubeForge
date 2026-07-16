namespace TubeForge.Downloads.Resume;

internal sealed record DownloadResumeState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string SourceIdentity { get; init; }

    public long? ExpectedLength { get; init; }

    public string? EntityTag { get; init; }

    public DateTimeOffset? LastModified { get; init; }
}
