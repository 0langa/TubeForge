using TubeForge.Core.Media;

namespace TubeForge.Downloads;

public sealed record DownloadRequest
{
    public const long DefaultSegmentedTransferMinimumBytes = 16 * 1024 * 1024;
    internal const long DefaultSegmentedTransferChunkBytes = 8 * 1024 * 1024;

    public required Uri SourceUrl { get; init; }

    public string? HttpUserAgent { get; init; }

    public required string SourceIdentity { get; init; }

    public required string DestinationPath { get; init; }

    public long? ExpectedLength { get; init; }

    public MediaContainer ExpectedContainer { get; init; } = MediaContainer.Unknown;

    public bool EnableSegmentedTransfer { get; init; }

    public int MaximumSegments { get; init; } = 4;

    public long SegmentedTransferMinimumBytes { get; init; } = DefaultSegmentedTransferMinimumBytes;

    internal long SegmentedTransferChunkBytes { get; init; } = DefaultSegmentedTransferChunkBytes;
}
