namespace TubeForge.Downloads;

public sealed record DownloadReceipt(
    string DestinationPath,
    long BytesWritten,
    bool Resumed);
