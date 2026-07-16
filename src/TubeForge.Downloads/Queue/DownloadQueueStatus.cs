namespace TubeForge.Downloads.Queue;

public enum DownloadQueueStatus
{
    Queued,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}
