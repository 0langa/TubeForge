using TubeForge.Core.Media;

namespace TubeForge.Downloads;

public sealed record DownloadRequest
{
    public required Uri SourceUrl { get; init; }

    public required string SourceIdentity { get; init; }

    public required string DestinationPath { get; init; }

    public long? ExpectedLength { get; init; }

    public MediaContainer ExpectedContainer { get; init; } = MediaContainer.Unknown;
}
