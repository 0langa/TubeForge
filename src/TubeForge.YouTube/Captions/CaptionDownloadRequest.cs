namespace TubeForge.YouTube.Captions;

public sealed record CaptionDownloadRequest
{
    public required Uri SourceUrl { get; init; }

    public required string DestinationPath { get; init; }

    public required CaptionOutputFormat OutputFormat { get; init; }
}

public sealed record CaptionDownloadReceipt(
    string DestinationPath,
    long BytesWritten,
    int CueCount);
