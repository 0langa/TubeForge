namespace TubeForge.Downloads;

public sealed record DownloadProgress(
    long BytesReceived,
    long? TotalBytes,
    double BytesPerSecond,
    TimeSpan? EstimatedRemaining)
{
    public double? Fraction => TotalBytes is > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes.Value, 0, 1)
        : null;
}
