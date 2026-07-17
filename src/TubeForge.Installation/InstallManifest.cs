namespace TubeForge.Installation;

public sealed record InstallManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string Product { get; init; }

    public required string Version { get; init; }

    public required IReadOnlyList<InstallFileEntry> Files { get; init; }
}

public sealed record InstallFileEntry
{
    public required string Path { get; init; }

    public long Length { get; init; }

    public required string Sha256 { get; init; }
}

public sealed record InstallPayloadReceipt(
    string StagingDirectory,
    Version Version,
    int FileCount,
    long BytesWritten);
