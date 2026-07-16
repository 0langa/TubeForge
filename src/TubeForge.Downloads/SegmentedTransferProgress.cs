namespace TubeForge.Downloads;

public static class SegmentedTransferProgress
{
    public static long? GetCompletedBytes(string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        return SegmentedDownloadStateStore.ReadCompletedBytes(destinationPath);
    }
}
