using TubeForge.Downloads.Queue;

namespace TubeForge.Downloads.History;

public enum DownloadDuplicateKind
{
    QueuedOutput,
    CompletedOutput,
    QueueDestination,
    HistoryDestination
}

public sealed record DownloadDuplicate(DownloadDuplicateKind Kind, string DisplayTitle);

public static class DownloadDuplicateDetector
{
    public static DownloadDuplicate? Find(
        string sourceIdentity,
        string destinationPath,
        IEnumerable<DownloadQueueItem> queueItems,
        IEnumerable<DownloadHistoryEntry> historyEntries)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentNullException.ThrowIfNull(queueItems);
        ArgumentNullException.ThrowIfNull(historyEntries);

        foreach (var item in queueItems.Where(item => item.Status != DownloadQueueStatus.Cancelled))
        {
            if (item.SourceIdentity.Equals(sourceIdentity, StringComparison.Ordinal))
            {
                return new DownloadDuplicate(DownloadDuplicateKind.QueuedOutput, item.DisplayTitle);
            }

            if (item.DestinationPath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return new DownloadDuplicate(DownloadDuplicateKind.QueueDestination, item.DisplayTitle);
            }
        }

        foreach (var entry in historyEntries)
        {
            if (entry.SourceIdentity.Equals(sourceIdentity, StringComparison.Ordinal))
            {
                return new DownloadDuplicate(DownloadDuplicateKind.CompletedOutput, entry.DisplayTitle);
            }

            if (entry.DestinationPath.Equals(destinationPath, StringComparison.OrdinalIgnoreCase))
            {
                return new DownloadDuplicate(DownloadDuplicateKind.HistoryDestination, entry.DisplayTitle);
            }
        }

        return null;
    }
}
