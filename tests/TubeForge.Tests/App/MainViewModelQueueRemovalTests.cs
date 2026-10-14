using System.Net.Http;
using System.Reflection;
using TubeForge.App.ViewModels;
using TubeForge.Core.Settings;
using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.App;

public static class MainViewModelQueueRemovalTests
{
    [Test]
    public static async Task RemovingAFailedOrCancelledItemDeletesItsLeftoversOnly()
    {
        var root = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", $"queue-removal-{Guid.NewGuid():N}");
        var applicationData = Path.Combine(root, "appdata");
        var downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(downloads);
        try
        {
            var settings = await new TubeForgeSettingsStore(Path.Combine(applicationData, "settings.json"))
                .SaveAsync(new TubeForgeSettings
                {
                    DownloadFolder = downloads,
                    EnableAutomaticUpdateChecks = false,
                    ResponsibleUseAccepted = true
                });
            Assert.True(settings.IsSuccess, settings.Error?.Message);

            var failed = Item(Path.Combine(downloads, "Failed.mp4"), DownloadQueueStatus.Failed);
            var cancelled = Item(Path.Combine(downloads, "Cancelled.mp4"), DownloadQueueStatus.Cancelled);
            var paused = Item(Path.Combine(downloads, "Paused.mp4"), DownloadQueueStatus.Paused);
            var queue = await new DownloadQueueStore(Path.Combine(applicationData, "queue.json"))
                .SaveAsync(new DownloadQueueSnapshot { Items = [failed, cancelled, paused] });
            Assert.True(queue.IsSuccess, queue.Error?.Message);

            string[] failedLeftovers = ["Failed.mp4.part", "Failed.mp4.part.segments.json", "Failed.mp4.video-track.mp4"];
            string[] cancelledLeftovers = ["Cancelled.mp4.part", "Cancelled.mp4.part.json"];
            string[] untouched = ["Failed.mp4", "Failed.mp4.notes.txt", "Paused.mp4.part", "Paused.mp4.part.json"];
            foreach (var name in failedLeftovers.Concat(cancelledLeftovers).Concat(untouched))
            {
                await File.WriteAllTextAsync(Path.Combine(downloads, name), name);
            }

            using var viewModel = CreateViewModel(applicationData);
            await viewModel.InitializeAsync();

            await RemoveAsync(viewModel, failed.Id);
            await RemoveAsync(viewModel, cancelled.Id);
            await RemoveAsync(viewModel, paused.Id);

            Assert.Equal(0, viewModel.QueueItems.Count);
            foreach (var name in failedLeftovers.Concat(cancelledLeftovers))
            {
                Assert.False(File.Exists(Path.Combine(downloads, name)), $"{name} should have been deleted with its item.");
            }

            foreach (var name in untouched)
            {
                Assert.True(File.Exists(Path.Combine(downloads, name)), $"{name} must be left alone.");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public static async Task RemovingAFailedItemKeepsFilesAnotherQueuedItemStillUses()
    {
        var root = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", $"queue-shared-{Guid.NewGuid():N}");
        var applicationData = Path.Combine(root, "appdata");
        var downloads = Path.Combine(root, "downloads");
        Directory.CreateDirectory(downloads);
        try
        {
            var settings = await new TubeForgeSettingsStore(Path.Combine(applicationData, "settings.json"))
                .SaveAsync(new TubeForgeSettings
                {
                    DownloadFolder = downloads,
                    EnableAutomaticUpdateChecks = false,
                    ResponsibleUseAccepted = true
                });
            Assert.True(settings.IsSuccess, settings.Error?.Message);

            // Two queue entries for one destination: the paused one still owns the
            // partial data, so removing the failed one must not delete it.
            var destination = Path.Combine(downloads, "Shared.mp4");
            var failed = Item(destination, DownloadQueueStatus.Failed);
            var paused = Item(destination, DownloadQueueStatus.Paused);
            var queue = await new DownloadQueueStore(Path.Combine(applicationData, "queue.json"))
                .SaveAsync(new DownloadQueueSnapshot { Items = [failed, paused] });
            Assert.True(queue.IsSuccess, queue.Error?.Message);

            string[] shared = ["Shared.mp4.part", "Shared.mp4.part.json"];
            foreach (var name in shared)
            {
                await File.WriteAllTextAsync(Path.Combine(downloads, name), name);
            }

            using var viewModel = CreateViewModel(applicationData);
            await viewModel.InitializeAsync();

            await RemoveAsync(viewModel, failed.Id);

            Assert.Equal(1, viewModel.QueueItems.Count);
            foreach (var name in shared)
            {
                Assert.True(File.Exists(Path.Combine(downloads, name)), $"{name} still belongs to the paused item.");
            }
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static MainViewModel CreateViewModel(string applicationData)
    {
        var constructor = typeof(MainViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(HttpMessageHandler), typeof(Version)],
            modifiers: null)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, ".ctor(string, HttpMessageHandler, Version)");
        return (MainViewModel)(constructor.Invoke([applicationData, UnreachableHttp.CreateHandler(), new Version(2, 2, 1)])
            ?? throw new InvalidOperationException("The view model was not created."));
    }

    private static async Task RemoveAsync(MainViewModel viewModel, Guid itemId)
    {
        var remove = typeof(MainViewModel).GetMethod(
            "RemoveQueueItemAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, "RemoveQueueItemAsync");
        await (Task)(remove.Invoke(viewModel, [itemId])
            ?? throw new InvalidOperationException("Remove returned no task."));
    }

    private static DownloadQueueItem Item(string destination, DownloadQueueStatus status)
    {
        var now = new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);
        return new DownloadQueueItem
        {
            Id = Guid.NewGuid(),
            VideoId = "Fixture123_",
            FormatId = 22,
            SourceIdentity = "Fixture123_:22",
            DisplayTitle = Path.GetFileNameWithoutExtension(destination),
            DestinationPath = destination,
            ExpectedLength = 1024,
            BytesReceived = 512,
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }
}
