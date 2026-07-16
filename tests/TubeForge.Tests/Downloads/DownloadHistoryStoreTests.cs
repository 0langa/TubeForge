using TubeForge.Downloads.History;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class DownloadHistoryStoreTests
{
    [Test]
    public static async Task PersistsValidatedHistoryAndReturnsEmptyWhenMissing()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "history.json");
        var store = new DownloadHistoryStore(path);
        var missing = await store.LoadAsync();
        var snapshot = new DownloadHistorySnapshot { Entries = [Entry(directory.Path)] };

        var saved = await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync();

        Assert.True(missing.IsSuccess, missing.Error?.Message);
        Assert.Equal(0, missing.Value.Entries.Count);
        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.Equal(1, loaded.Value.Entries.Count);
        Assert.Equal("Fixture title", loaded.Value.Entries[0].DisplayTitle);
        Assert.Equal(42L, loaded.Value.Entries[0].BytesWritten);
    }

    [Test]
    public static async Task RejectsInvalidEntriesAndLeavesMalformedPrimaryUnchanged()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "history.json");
        var store = new DownloadHistoryStore(path);
        var invalid = await store.SaveAsync(new DownloadHistorySnapshot
        {
            Entries = [Entry(directory.Path) with { SourceIdentity = "invalid" }]
        });
        await File.WriteAllTextAsync(path, "{ malformed");

        var corrupt = await store.LoadAsync();

        Assert.False(invalid.IsSuccess);
        Assert.Equal("History.InvalidState", invalid.Error?.Code);
        Assert.False(corrupt.IsSuccess);
        Assert.Equal("History.Corrupt", corrupt.Error?.Code);
        Assert.Equal("{ malformed", await File.ReadAllTextAsync(path));
    }

    [Test]
    public static async Task RecoversValidPendingSnapshotWhenPrimaryIsCorrupt()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "history.json");
        var store = new DownloadHistoryStore(path);
        await File.WriteAllTextAsync(path, "{ corrupt");
        var pendingStore = new DownloadHistoryStore(path + ".new");
        var saved = await pendingStore.SaveAsync(new DownloadHistorySnapshot
        {
            Entries = [Entry(directory.Path)]
        });

        var recovered = await store.LoadAsync();

        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.True(recovered.IsSuccess, recovered.Error?.Message);
        Assert.Equal(1, recovered.Value.Entries.Count);
    }

    private static DownloadHistoryEntry Entry(string directory) => new()
    {
        Id = Guid.NewGuid(),
        VideoId = "Video000001",
        SourceIdentity = "Video000001:22",
        DisplayTitle = "Fixture title",
        DestinationPath = Path.GetFullPath(Path.Combine(directory, "fixture.mp4")),
        BytesWritten = 42,
        CompletedAtUtc = DateTimeOffset.UtcNow
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
