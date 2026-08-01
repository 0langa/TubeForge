using TubeForge.Downloads.History;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class LibraryRescannerTests
{
    [Test]
    public static async Task RepairsOnlyUniqueNameAndSizeMatches()
    {
        using var directory = new TestDirectory();
        var unique = Path.Combine(directory.Path, "moved", "unique.mp4");
        var duplicateOne = Path.Combine(directory.Path, "one", "duplicate.mp4");
        var duplicateTwo = Path.Combine(directory.Path, "two", "duplicate.mp4");
        Directory.CreateDirectory(Path.GetDirectoryName(unique)!);
        Directory.CreateDirectory(Path.GetDirectoryName(duplicateOne)!);
        Directory.CreateDirectory(Path.GetDirectoryName(duplicateTwo)!);
        await File.WriteAllBytesAsync(unique, new byte[1_024]);
        await File.WriteAllBytesAsync(duplicateOne, new byte[2_048]);
        await File.WriteAllBytesAsync(duplicateTwo, new byte[2_048]);
        var missingRoot = Path.Combine(directory.Path, "old");
        var snapshot = new DownloadHistorySnapshot
        {
            Entries =
            [
                Entry(Guid.NewGuid(), Path.Combine(missingRoot, "unique.mp4"), 1_024),
                Entry(Guid.NewGuid(), Path.Combine(missingRoot, "duplicate.mp4"), 2_048),
                Entry(Guid.NewGuid(), Path.Combine(missingRoot, "absent.mp4"), 4_096)
            ]
        };

        var result = LibraryRescanner.Rescan(snapshot, directory.Path);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(3, result.Value.FilesScanned);
        Assert.Equal(1, result.Value.RecordsRepaired);
        Assert.Equal(1, result.Value.AmbiguousMatches);
        Assert.Equal(unique, result.Value.Snapshot.Entries[0].DestinationPath);
        Assert.Equal(snapshot.Entries[1].DestinationPath, result.Value.Snapshot.Entries[1].DestinationPath);
        Assert.Equal(snapshot.Entries[2].DestinationPath, result.Value.Snapshot.Entries[2].DestinationPath);
    }

    private static DownloadHistoryEntry Entry(Guid id, string destination, long bytes) => new()
    {
        Id = id,
        VideoId = "Fixture123_",
        SourceIdentity = "Fixture123_:18",
        DisplayTitle = "Fixture",
        DestinationPath = destination,
        BytesWritten = bytes,
        CompletedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
    };

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests.LibraryRescan"));

        public TestDirectory()
        {
            Directory.CreateDirectory(SafeRoot);
            Path = System.IO.Path.Combine(SafeRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            if (resolved.StartsWith(SafeRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
