using TubeForge.Downloads.History;
using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class DownloadDuplicateDetectorTests
{
    [Test]
    public static void FindsQueuedCompletedAndDestinationDuplicates()
    {
        var queue = new[] { QueueItem("Video000001:22", "first.mp4") };
        var history = new[] { HistoryEntry("Video000002:22", "second.mp4") };

        var queued = DownloadDuplicateDetector.Find(
            "Video000001:22",
            FullPath("new.mp4"),
            queue,
            history);
        var completed = DownloadDuplicateDetector.Find(
            "Video000002:22",
            FullPath("new.mp4"),
            queue,
            history);
        var destination = DownloadDuplicateDetector.Find(
            "Video000003:22",
            FullPath("second.mp4"),
            queue,
            history);

        Assert.Equal(DownloadDuplicateKind.QueuedOutput, queued?.Kind);
        Assert.Equal(DownloadDuplicateKind.CompletedOutput, completed?.Kind);
        Assert.Equal(DownloadDuplicateKind.HistoryDestination, destination?.Kind);
    }

    [Test]
    public static void IgnoresCancelledQueueItemsAndDifferentOutputsForSameVideo()
    {
        var cancelled = QueueItem("Video000001:22", "first.mp4") with
        {
            Status = DownloadQueueStatus.Cancelled
        };

        var result = DownloadDuplicateDetector.Find(
            "Video000001:140+140",
            FullPath("different.mp4"),
            [cancelled],
            []);

        Assert.Equal<DownloadDuplicate?>(null, result);
    }

    private static DownloadQueueItem QueueItem(string identity, string fileName) => new()
    {
        Id = Guid.NewGuid(),
        VideoId = identity[..identity.IndexOf(':')],
        FormatId = int.Parse(identity[(identity.IndexOf(':') + 1)..].Split('+')[0]),
        SourceIdentity = identity,
        DisplayTitle = "Queue fixture",
        DestinationPath = FullPath(fileName),
        BytesReceived = 0,
        Status = DownloadQueueStatus.Queued,
        CreatedAtUtc = DateTimeOffset.UtcNow,
        UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static DownloadHistoryEntry HistoryEntry(string identity, string fileName) => new()
    {
        Id = Guid.NewGuid(),
        VideoId = identity[..identity.IndexOf(':')],
        SourceIdentity = identity,
        DisplayTitle = "History fixture",
        DestinationPath = FullPath(fileName),
        BytesWritten = 42,
        CompletedAtUtc = DateTimeOffset.UtcNow
    };

    private static string FullPath(string fileName) =>
        Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TubeForge.Tests", fileName));
}
