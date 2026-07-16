namespace TubeForge.YouTube.Sidecars;

public sealed record ThumbnailDownloadRequest
{
    public required Uri SourceUrl { get; init; }

    public required string DestinationPath { get; init; }
}

public sealed record ThumbnailDownloadReceipt(
    string DestinationPath,
    long BytesWritten,
    string MediaType);
