using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class DownloadQueueStoreTests
{
    [Test]
    public static async Task PersistsPrivacySafeItemsAndRecoversInterruptedDownloads()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "queue.json");
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var store = new DownloadQueueStore(path);
        var snapshot = new DownloadQueueSnapshot
        {
            Items =
            [
                Item(Guid.Parse("11111111-1111-1111-1111-111111111111"), DownloadQueueStatus.Queued, now),
                Item(Guid.Parse("22222222-2222-2222-2222-222222222222"), DownloadQueueStatus.Downloading, now)
            ]
        };

        var saveResult = await store.SaveAsync(snapshot);

        Assert.True(saveResult.IsSuccess, saveResult.Error?.Message);
        var json = await File.ReadAllTextAsync(path);
        Assert.False(json.Contains("googlevideo", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("sourceUrl", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("expire=", StringComparison.OrdinalIgnoreCase));

        var loadResult = await store.LoadAsync();

        Assert.True(loadResult.IsSuccess, loadResult.Error?.Message);
        Assert.Equal(2, loadResult.Value.Items.Count);
        Assert.Equal(DownloadQueueStatus.Queued, loadResult.Value.Items[0].Status);
        Assert.Equal(DownloadQueueStatus.Paused, loadResult.Value.Items[1].Status);
        Assert.Equal("Fixture123_:22", loadResult.Value.Items[1].SourceIdentity);
        Assert.Equal(512L, loadResult.Value.Items[1].BytesReceived);
    }

    [Test]
    public static async Task RejectsUnsupportedSchemaAndInvalidItems()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

        var unsupported = await store.SaveAsync(new DownloadQueueSnapshot
        {
            SchemaVersion = DownloadQueueSnapshot.CurrentSchemaVersion + 1,
            Items = []
        });
        Assert.False(unsupported.IsSuccess);
        Assert.Equal("Queue.UnsupportedSchema", unsupported.Error?.Code);

        var invalid = await store.SaveAsync(new DownloadQueueSnapshot
        {
            Items = [Item(Guid.Empty, DownloadQueueStatus.Queued, now)]
        });
        Assert.False(invalid.IsSuccess);
        Assert.Equal("Queue.InvalidState", invalid.Error?.Code);
    }

    [Test]
    public static async Task ReportsCorruptQueueWithoutOverwritingIt()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "queue.json");
        await File.WriteAllTextAsync(path, "{not-json");
        var store = new DownloadQueueStore(path);

        var result = await store.LoadAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("Queue.Corrupt", result.Error?.Code);
        Assert.Equal("{not-json", await File.ReadAllTextAsync(path));
    }

    [Test]
    public static async Task ReturnsTypedCancellationBeforeTakingStorageLock()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var loadResult = await store.LoadAsync(cancellation.Token);
        var saveResult = await store.SaveAsync(new DownloadQueueSnapshot(), cancellation.Token);

        Assert.False(loadResult.IsSuccess);
        Assert.Equal("Operation.Cancelled", loadResult.Error?.Code);
        Assert.False(saveResult.IsSuccess);
        Assert.Equal("Operation.Cancelled", saveResult.Error?.Code);
    }

    [Test]
    public static async Task RejectsSourceIdentityThatDoesNotMatchPersistedSelection()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var now = DateTimeOffset.UtcNow;
        var mismatched = Item(Guid.NewGuid(), DownloadQueueStatus.Queued, now) with
        {
            SourceIdentity = "Fixture123_:401+140"
        };

        var result = await store.SaveAsync(new DownloadQueueSnapshot { Items = [mismatched] });

        Assert.False(result.IsSuccess);
        Assert.Equal("Queue.InvalidState", result.Error?.Code);
    }

    private static DownloadQueueItem Item(
        Guid id,
        DownloadQueueStatus status,
        DateTimeOffset now) => new()
        {
            Id = id,
            VideoId = "Fixture123_",
            FormatId = 22,
            SourceIdentity = "Fixture123_:22",
            DisplayTitle = "Synthetic fixture",
            DestinationPath = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", "fixture.mp4"),
            ExpectedLength = 1024,
            BytesReceived = 512,
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests"));

        public TestDirectory()
        {
            Path = System.IO.Path.Combine(SafeRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            if (!resolved.StartsWith(SafeRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to clean a test directory outside the safe root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
