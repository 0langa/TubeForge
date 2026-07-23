using System.Text.Json;
using TubeForge.Downloads.History;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class LibraryTransferServiceTests
{
    [Test]
    public static async Task ExportsImportsMigratesAndMergesWithoutDuplicates()
    {
        using var directory = new TestDirectory();
        var service = new LibraryTransferService();
        var original = Snapshot(Entry(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "Fixture123_:18",
            Path.Combine(directory.Path, "one.mp4"),
            DateTimeOffset.UtcNow.AddMinutes(-2)));
        var path = Path.Combine(directory.Path, "library.json");

        var exported = await service.ExportAsync(original, path);
        var imported = await service.ImportAsync(path);

        Assert.True(exported.IsSuccess, exported.Error?.Message);
        Assert.True(imported.IsSuccess, imported.Error?.Message);
        Assert.Equal(original.Entries.Single(), imported.Value.Entries.Single());
        var json = await File.ReadAllTextAsync(path);
        Assert.True(json.Contains("\"schemaVersion\": 2", StringComparison.Ordinal));

        var legacyPath = Path.Combine(directory.Path, "legacy.json");
        await File.WriteAllTextAsync(legacyPath, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            entries = original.Entries
        }));
        var legacy = await service.ImportAsync(legacyPath);
        Assert.True(legacy.IsSuccess, legacy.Error?.Message);

        var newer = original.Entries.Single() with
        {
            Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
            CompletedAtUtc = DateTimeOffset.UtcNow,
            BytesWritten = 2_048
        };
        var merged = LibraryTransferService.Merge(original, Snapshot(newer));
        Assert.True(merged.IsSuccess, merged.Error?.Message);
        Assert.Equal(1, merged.Value.Entries.Count);
        Assert.Equal(2_048L, merged.Value.Entries.Single().BytesWritten);
    }

    [Test]
    public static async Task RejectsMalformedOversizedAndInvalidImportsWithoutMutation()
    {
        using var directory = new TestDirectory();
        var service = new LibraryTransferService();
        var malformed = Path.Combine(directory.Path, "malformed.json");
        await File.WriteAllTextAsync(malformed, "{not json");
        Assert.Equal("Library.InvalidImport", (await service.ImportAsync(malformed)).Error?.Code);

        var invalid = Path.Combine(directory.Path, "invalid.json");
        await File.WriteAllTextAsync(invalid, """
            {"schemaVersion":2,"exportedAtUtc":"2026-01-01T00:00:00Z","entries":[]}
            """);
        var validEmpty = await service.ImportAsync(invalid);
        Assert.True(validEmpty.IsSuccess, validEmpty.Error?.Message);

        await File.WriteAllTextAsync(invalid, """
            {"schemaVersion":99,"exportedAtUtc":"2026-01-01T00:00:00Z","entries":[]}
            """);
        Assert.Equal("Library.InvalidImport", (await service.ImportAsync(invalid)).Error?.Code);

        var oversized = Path.Combine(directory.Path, "oversized.json");
        await File.WriteAllTextAsync(oversized, new string('x', 8 * 1024 * 1024 + 1));
        Assert.Equal("Library.InvalidImport", (await service.ImportAsync(oversized)).Error?.Code);
    }

    private static DownloadHistorySnapshot Snapshot(params DownloadHistoryEntry[] entries) => new()
    {
        Entries = entries
    };

    private static DownloadHistoryEntry Entry(
        Guid id,
        string identity,
        string destination,
        DateTimeOffset completedAt) => new()
    {
        Id = id,
        VideoId = "Fixture123_",
        SourceIdentity = identity,
        DisplayTitle = "Fixture",
        DestinationPath = destination,
        BytesWritten = 1_024,
        CompletedAtUtc = completedAt
    };

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests.LibraryTransfer"));

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
