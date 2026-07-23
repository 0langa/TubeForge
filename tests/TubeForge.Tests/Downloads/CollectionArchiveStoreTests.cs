using TubeForge.Core.Files;
using TubeForge.Core.YouTube;
using TubeForge.Downloads.Archives;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class CollectionArchiveStoreTests
{
    [Test]
    public static async Task PersistsValidatedProfilesAndCheckedItemSet()
    {
        using var directory = new TestDirectory();
        var store = new CollectionArchiveStore(Path.Combine(directory.Path, "archives.json"));
        var profile = Profile();

        var saved = await store.SaveAsync(new CollectionArchiveSnapshot { Profiles = [profile] });
        var loaded = await store.LoadAsync();

        Assert.True(saved.IsSuccess, saved.Error?.Message);
        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        var reloaded = loaded.Value.Profiles.Single();
        Assert.Equal(profile.Id, reloaded.Id);
        Assert.Equal(profile.SourceUrl, reloaded.SourceUrl);
        Assert.Equal(profile.OutputPreset, reloaded.OutputPreset);
        Assert.Equal(profile.CaptionPreference, reloaded.CaptionPreference);
        Assert.Equal(profile.LastCheckedVideoIds.Single(), reloaded.LastCheckedVideoIds.Single());
        var json = await File.ReadAllTextAsync(store.StoragePath);
        Assert.False(json.Contains("googlevideo", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("cookie", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public static async Task RejectsDuplicateSourcesUnsafeTemplatesAndMalformedState()
    {
        using var directory = new TestDirectory();
        var store = new CollectionArchiveStore(Path.Combine(directory.Path, "archives.json"));
        var profile = Profile();
        var duplicate = profile with { Id = Guid.NewGuid() };
        Assert.Equal(
            "Archive.InvalidState",
            (await store.SaveAsync(new CollectionArchiveSnapshot { Profiles = [profile, duplicate] })).Error?.Code);

        var unsafeTemplate = profile with { FileNameTemplate = "{unknown}" };
        Assert.Equal(
            "Archive.InvalidState",
            (await store.SaveAsync(new CollectionArchiveSnapshot { Profiles = [unsafeTemplate] })).Error?.Code);

        await File.WriteAllTextAsync(store.StoragePath, "{not json");
        Assert.Equal("Archive.Corrupt", (await store.LoadAsync()).Error?.Code);
    }

    private static CollectionArchiveProfile Profile() => new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        SourceKind = YouTubeCollectionKind.Playlist,
        SourceUrl = "https://www.youtube.com/playlist?list=PL1234567890",
        DisplayName = "Fixture playlist",
        DestinationPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TubeForgeArchive")),
        FileNameTemplate = FileNameTemplate.Default,
        OutputPreset = ArchiveOutputPreset.WindowsCompatibleMp4,
        CaptionPreference = ArchiveCaptionPreference.ManualPreferred,
        EmbedChapters = true,
        LastCheckedVideoIds = ["Fixture123_"],
        CreatedAtUtc = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero),
        LastCheckedAtUtc = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero)
    };

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests.Archives"));

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
