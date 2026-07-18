using TubeForge.Core.Media;

namespace TubeForge.Downloads;

public sealed record AdaptiveDownloadRequest
{
    public required DownloadRequest Video { get; init; }

    public required DownloadRequest Audio { get; init; }

    public required string DestinationPath { get; init; }

    public required MediaContainer OutputContainer { get; init; }

    public bool AllowExistingValidatedOutput { get; init; }
}

public sealed record AdaptiveDownloadReceipt(
    string DestinationPath,
    long BytesWritten,
    long VideoBytes,
    long AudioBytes);
