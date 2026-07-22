using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;
using TubeForge.Core.Media;
using System.Text.Json;

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
    public static async Task MigratesPublishedSchemaOneQueueAndPreservesDownloadState()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "queue.json");
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var legacy = new
        {
            schemaVersion = 1,
            items = new[]
            {
                new
                {
                    id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                    videoId = "Fixture123_",
                    formatId = 22,
                    sourceIdentity = "Fixture123_:22",
                    displayTitle = "Synthetic fixture",
                    destinationPath = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", "fixture.mp4"),
                    expectedLength = 1024L,
                    bytesReceived = 512L,
                    status = DownloadQueueStatus.Paused,
                    createdAtUtc = now,
                    updatedAtUtc = now,
                    failureCode = (string?)null
                }
            }
        };
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(legacy));

        var loaded = await new DownloadQueueStore(path).LoadAsync();

        Assert.True(loaded.IsSuccess, loaded.Error?.Message);
        Assert.Equal(DownloadQueueSnapshot.CurrentSchemaVersion, loaded.Value.SchemaVersion);
        Assert.Equal(0, loaded.Value.Items[0].AttemptCount);
        Assert.Equal(512L, loaded.Value.Items[0].BytesReceived);
        Assert.Equal(DownloadQueueStatus.Paused, loaded.Value.Items[0].Status);
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

    [Test]
    public static async Task PersistsEveryConvertedOutputProfileAcrossRestart()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "queue.json");
        var store = new DownloadQueueStore(path);
        var profiles = new[]
        {
            OutputProfile.Mp3(320),
            OutputProfile.Aac(256),
            OutputProfile.Opus(160),
            OutputProfile.Wav,
            OutputProfile.Flac,
            OutputProfile.H264AacMp4,
            OutputProfile.H265AacMp4,
            OutputProfile.Vp9OpusWebM
        };
        var items = profiles.Select(profile => Item(
                Guid.NewGuid(),
                DownloadQueueStatus.Queued,
                DateTimeOffset.UtcNow) with
        {
            FormatId = 140,
            SourceIdentity = $"Fixture123_:140@{profile.Identity}",
            DestinationPath = Path.Combine(
                    Path.GetTempPath(),
                    "TubeForge.Tests",
                    "fixture-" + profile.Identity + profile.Extension)
        }).ToArray();

        var save = await store.SaveAsync(new DownloadQueueSnapshot { Items = items });
        Assert.True(save.IsSuccess, save.Error?.Message);

        var load = await new DownloadQueueStore(path).LoadAsync();
        Assert.True(load.IsSuccess, load.Error?.Message);
        Assert.Equal(profiles.Length, load.Value.Items.Count);
        for (var index = 0; index < profiles.Length; index++)
        {
            Assert.Equal($"Fixture123_:140@{profiles[index].Identity}", load.Value.Items[index].SourceIdentity);
        }
    }

    [Test]
    public static async Task PersistsCompletedMp3WhenConvertedOutputIsLargerThanSource()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var now = DateTimeOffset.UtcNow;
        var completed = Item(Guid.NewGuid(), DownloadQueueStatus.Completed, now) with
        {
            FormatId = 140,
            SourceIdentity = "Fixture123_:140@mp3-192",
            DestinationPath = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", "fixture.mp3"),
            ExpectedLength = 1_024,
            BytesReceived = 1_536
        };

        var save = await store.SaveAsync(new DownloadQueueSnapshot { Items = [completed] });

        Assert.True(save.IsSuccess, save.Error?.Message);
        var load = await new DownloadQueueStore(store.StoragePath).LoadAsync();
        Assert.True(load.IsSuccess, load.Error?.Message);
        Assert.Equal(1_536L, load.Value.Items[0].BytesReceived);
        Assert.Equal(1_536L, load.Value.Items[0].ExpectedLength);
    }

    [Test]
    public static async Task PersistsCaptionEmbedAndAcceptsLargerCompletedMedia()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var now = DateTimeOffset.UtcNow;
        var completed = Item(Guid.NewGuid(), DownloadQueueStatus.Completed, now) with
        {
            FormatId = 18,
            SourceIdentity = "Fixture123_:18~m.en-US",
            ExpectedLength = 1_024,
            BytesReceived = 1_536
        };

        var save = await store.SaveAsync(new DownloadQueueSnapshot { Items = [completed] });

        Assert.True(save.IsSuccess, save.Error?.Message);
        var load = await new DownloadQueueStore(store.StoragePath).LoadAsync();
        Assert.True(load.IsSuccess, load.Error?.Message);
        Assert.Equal("Fixture123_:18~m.en-US", load.Value.Items[0].SourceIdentity);
        Assert.Equal(1_536L, load.Value.Items[0].ExpectedLength);
    }

    [Test]
    public static async Task PersistsChapterEmbedAndAcceptsLargerCompletedMedia()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var now = DateTimeOffset.UtcNow;
        var completed = Item(Guid.NewGuid(), DownloadQueueStatus.Completed, now) with
        {
            FormatId = 18,
            SourceIdentity = "Fixture123_:18^chapters",
            ExpectedLength = 1_024,
            BytesReceived = 1_536
        };

        var save = await store.SaveAsync(new DownloadQueueSnapshot { Items = [completed] });

        Assert.True(save.IsSuccess, save.Error?.Message);
        var load = await new DownloadQueueStore(store.StoragePath).LoadAsync();
        Assert.True(load.IsSuccess, load.Error?.Message);
        Assert.Equal("Fixture123_:18^chapters", load.Value.Items[0].SourceIdentity);
        Assert.Equal(1_536L, load.Value.Items[0].ExpectedLength);
    }

    [Test]
    public static async Task PersistsTrimRangeAcrossQueueRestart()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var queued = Item(Guid.NewGuid(), DownloadQueueStatus.Queued, DateTimeOffset.UtcNow) with
        {
            FormatId = 18,
            SourceIdentity = "Fixture123_:18%5000-60000"
        };

        var save = await store.SaveAsync(new DownloadQueueSnapshot { Items = [queued] });

        Assert.True(save.IsSuccess, save.Error?.Message);
        var load = await new DownloadQueueStore(store.StoragePath).LoadAsync();
        Assert.True(load.IsSuccess, load.Error?.Message);
        Assert.Equal("Fixture123_:18%5000-60000", load.Value.Items[0].SourceIdentity);
    }

    [Test]
    public static async Task PersistsSponsorBlockSelectionWithoutSegmentPayloads()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var identity = "Fixture123_:18&chapters.sponsor,intro";
        var queued = Item(Guid.NewGuid(), DownloadQueueStatus.Completed, DateTimeOffset.UtcNow) with
        {
            FormatId = 18,
            SourceIdentity = identity,
            ExpectedLength = 1_024,
            BytesReceived = 1_536
        };

        var save = await store.SaveAsync(new DownloadQueueSnapshot { Items = [queued] });

        Assert.True(save.IsSuccess, save.Error?.Message);
        var load = await new DownloadQueueStore(store.StoragePath).LoadAsync();
        Assert.True(load.IsSuccess, load.Error?.Message);
        Assert.Equal(identity, load.Value.Items[0].SourceIdentity);
        Assert.Equal(1_536L, load.Value.Items[0].ExpectedLength);
        var json = await File.ReadAllTextAsync(store.StoragePath);
        Assert.False(json.Contains("segment", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public static async Task PersistsLiveCaptureLimitsWithoutManifestUrls()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var identity = "Fixture123_:1000001!3600-4294967296-21600";
        var completed = Item(Guid.NewGuid(), DownloadQueueStatus.Completed, DateTimeOffset.UtcNow) with
        {
            FormatId = 1_000_001,
            SourceIdentity = identity,
            ExpectedLength = null,
            BytesReceived = 8_192
        };

        var save = await store.SaveAsync(new DownloadQueueSnapshot { Items = [completed] });

        Assert.True(save.IsSuccess, save.Error?.Message);
        var load = await new DownloadQueueStore(store.StoragePath).LoadAsync();
        Assert.True(load.IsSuccess, load.Error?.Message);
        Assert.Equal(identity, load.Value.Items[0].SourceIdentity);
        Assert.Equal(8_192L, load.Value.Items[0].ExpectedLength);
        var json = await File.ReadAllTextAsync(store.StoragePath);
        Assert.False(json.Contains("http", StringComparison.OrdinalIgnoreCase));
        Assert.False(json.Contains("manifest", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public static async Task RejectsOversizedMp3BeforeConversionCompletes()
    {
        using var directory = new TestDirectory();
        var store = new DownloadQueueStore(Path.Combine(directory.Path, "queue.json"));
        var now = DateTimeOffset.UtcNow;
        var downloading = Item(Guid.NewGuid(), DownloadQueueStatus.Downloading, now) with
        {
            FormatId = 140,
            SourceIdentity = "Fixture123_:140@mp3-192",
            DestinationPath = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", "fixture.mp3"),
            ExpectedLength = 1_024,
            BytesReceived = 1_536
        };

        var save = await store.SaveAsync(new DownloadQueueSnapshot { Items = [downloading] });

        Assert.False(save.IsSuccess);
        Assert.Equal("Queue.InvalidState", save.Error?.Code);
    }

    [Test]
    public static async Task RecoversFlushedPendingStateAfterInterruptedReplacement()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "queue.json");
        var store = new DownloadQueueStore(path);
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var original = new DownloadQueueSnapshot
        {
            Items = [Item(Guid.Parse("11111111-1111-1111-1111-111111111111"), DownloadQueueStatus.Queued, now)]
        };
        var pending = new DownloadQueueSnapshot
        {
            Items = [Item(Guid.Parse("22222222-2222-2222-2222-222222222222"), DownloadQueueStatus.Downloading, now)]
        };

        Assert.True((await store.SaveAsync(original)).IsSuccess);
        Assert.True((await new DownloadQueueStore(path + ".new").SaveAsync(pending)).IsSuccess);
        await File.WriteAllTextAsync(path, "{ interrupted replacement");

        var recovered = await store.LoadAsync();

        Assert.True(recovered.IsSuccess, recovered.Error?.Message);
        Assert.Equal(1, recovered.Value.Items.Count);
        Assert.Equal(pending.Items[0].Id, recovered.Value.Items[0].Id);
        Assert.Equal(DownloadQueueStatus.Paused, recovered.Value.Items[0].Status);
        Assert.Equal("{ interrupted replacement", await File.ReadAllTextAsync(path));
    }

    [Test]
    public static async Task RecoversLastCommittedBackupWhenPrimaryAndPendingAreDamaged()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "queue.json");
        var store = new DownloadQueueStore(path);
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");

        Assert.True((await store.SaveAsync(new DownloadQueueSnapshot
        {
            Items = [Item(firstId, DownloadQueueStatus.Queued, now)]
        })).IsSuccess);
        Assert.True((await store.SaveAsync(new DownloadQueueSnapshot
        {
            Items = [Item(Guid.Parse("22222222-2222-2222-2222-222222222222"), DownloadQueueStatus.Queued, now)]
        })).IsSuccess);
        await File.WriteAllTextAsync(path, "{\"schemaVersion\":1,\"items\":null}");
        await File.WriteAllTextAsync(path + ".new", "{ damaged pending");

        var recovered = await store.LoadAsync();

        Assert.True(recovered.IsSuccess, recovered.Error?.Message);
        Assert.Equal(firstId, recovered.Value.Items[0].Id);
    }

    [Test]
    public static async Task RepeatedAtomicSavesRemainReadableAndBounded()
    {
        using var directory = new TestDirectory();
        var path = Path.Combine(directory.Path, "queue.json");
        var store = new DownloadQueueStore(path);
        var now = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

        for (var iteration = 0; iteration < 128; iteration++)
        {
            var items = Enumerable.Range(0, 32)
                .Select(index => Item(
                    DeterministicId(iteration, index),
                    index % 3 == 0 ? DownloadQueueStatus.Downloading : DownloadQueueStatus.Queued,
                    now.AddSeconds(iteration)))
                .ToArray();
            var saved = await store.SaveAsync(new DownloadQueueSnapshot { Items = items });
            var loaded = await store.LoadAsync();

            Assert.True(saved.IsSuccess, saved.Error?.Message);
            Assert.True(loaded.IsSuccess, loaded.Error?.Message);
            Assert.Equal(items.Length, loaded.Value.Items.Count);
            Assert.Equal(items[0].Id, loaded.Value.Items[0].Id);
            Assert.True(loaded.Value.Items.All(item => item.Status != DownloadQueueStatus.Downloading));
            Assert.False(File.Exists(path + ".new"));
        }
    }

    private static Guid DeterministicId(int iteration, int index)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, iteration + 1);
        BitConverter.TryWriteBytes(bytes[4..], index + 1);
        BitConverter.TryWriteBytes(bytes[8..], ((long)iteration << 32) | (uint)index | 1L);
        return new Guid(bytes);
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
