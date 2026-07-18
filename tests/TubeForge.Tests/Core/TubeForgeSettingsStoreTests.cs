using TubeForge.Core.Settings;
using TubeForge.Tests.Framework;
using System.Text.Json;

namespace TubeForge.Tests.Core;

public static class TubeForgeSettingsStoreTests
{
    [Test]
    public static async Task ReturnsDefaultsThenPersistsValidatedSettings()
    {
        using var directory = new TestDirectory();
        var store = new TubeForgeSettingsStore(Path.Combine(directory.Path, "settings.json"));
        var defaults = Settings(directory.Path);

        var missing = await store.LoadAsync(defaults);
        var save = await store.SaveAsync(defaults with
        {
            MaximumConcurrentDownloads = 4,
            FileNameTemplate = "{channel} - {title} [{quality}]",
            EnableSegmentedTransfers = true,
            EnableAutomaticUpdateChecks = false,
            ResponsibleUseAccepted = true
        });
        var loaded = await store.LoadAsync(defaults);

        Assert.True(missing.IsSuccess);
        Assert.Equal(2, missing.Value.MaximumConcurrentDownloads);
        Assert.True(save.IsSuccess);
        Assert.True(loaded.IsSuccess);
        Assert.Equal(4, loaded.Value.MaximumConcurrentDownloads);
        Assert.Equal("{channel} - {title} [{quality}]", loaded.Value.FileNameTemplate);
        Assert.True(loaded.Value.EnableSegmentedTransfers);
        Assert.False(loaded.Value.EnableAutomaticUpdateChecks);
        Assert.True(loaded.Value.ResponsibleUseAccepted);
    }

    [Test]
    public static async Task RejectsInvalidValuesAndLeavesMalformedFileUntouched()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new TubeForgeSettingsStore(path);
        var invalid = await store.SaveAsync(Settings(directory.Path) with { MaximumConcurrentDownloads = 5 });
        var unsafePath = await store.SaveAsync(Settings(directory.Path) with
        {
            DownloadFolder = Path.GetFullPath(directory.Path) + "\nprivate"
        });
        var invalidTemplate = await store.SaveAsync(Settings(directory.Path) with
        {
            FileNameTemplate = "{unknown}"
        });
        await File.WriteAllTextAsync(path, "{ malformed");

        var corrupt = await store.LoadAsync(Settings(directory.Path));

        Assert.False(invalid.IsSuccess);
        Assert.Equal("Settings.InvalidState", invalid.Error?.Code);
        Assert.False(unsafePath.IsSuccess);
        Assert.Equal("Settings.InvalidState", unsafePath.Error?.Code);
        Assert.False(invalidTemplate.IsSuccess);
        Assert.Equal("Settings.InvalidState", invalidTemplate.Error?.Code);
        Assert.False(corrupt.IsSuccess);
        Assert.Equal("Settings.Corrupt", corrupt.Error?.Code);
        Assert.Equal("{ malformed", await File.ReadAllTextAsync(path));
    }

    [Test]
    public static async Task MigratesPublishedSchemaOneSettingsWithStableDefaults()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            downloadFolder = Path.GetFullPath(directory.Path),
            maximumConcurrentDownloads = 3,
            fileNameTemplate = "{title}",
            enableSegmentedTransfers = true,
            enableAutomaticUpdateChecks = false,
            responsibleUseAccepted = true
        }));

        var loaded = await new TubeForgeSettingsStore(path).LoadAsync(Settings(directory.Path));

        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.Equal(TubeForgeSettings.CurrentSchemaVersion, loaded.Value.SchemaVersion);
        Assert.Equal(LibrarySortOrder.NewestFirst, loaded.Value.LibrarySortOrder);
        Assert.Equal(3, loaded.Value.MaximumConcurrentDownloads);
        Assert.True(loaded.Value.EnableSegmentedTransfers);
    }

    private static TubeForgeSettings Settings(string folder) => new()
    {
        DownloadFolder = Path.GetFullPath(folder),
        MaximumConcurrentDownloads = 2
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
