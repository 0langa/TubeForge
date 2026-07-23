using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Files;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;

namespace TubeForge.Downloads.Archives;

public sealed class CollectionArchiveStore
{
    public const int MaximumProfiles = 50;
    public const int MaximumCheckedItems = 5_000;
    private const long MaximumFileBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 24,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _storageLock = new(1, 1);

    public CollectionArchiveStore(string storagePath)
    {
        StoragePath = Path.GetFullPath(storagePath);
    }

    public string StoragePath { get; }

    public async Task<Result<CollectionArchiveSnapshot>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await _storageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(StoragePath))
            {
                return Result<CollectionArchiveSnapshot>.Success(new CollectionArchiveSnapshot());
            }

            if (new FileInfo(StoragePath).Length > MaximumFileBytes)
            {
                return Corrupt();
            }

            await using var stream = new FileStream(
                StoragePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<CollectionArchiveSnapshot>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return snapshot is not null && Validate(snapshot) is null
                ? Result<CollectionArchiveSnapshot>.Success(snapshot)
                : Corrupt();
        }
        catch (JsonException)
        {
            return Corrupt();
        }
        catch (OperationCanceledException)
        {
            return Failure<CollectionArchiveSnapshot>("Operation.Cancelled", "Archive profile loading was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure<CollectionArchiveSnapshot>(
                "Archive.LoadFailed",
                "TubeForge could not load archive profiles.",
                exception.GetType().Name);
        }
        finally
        {
            _storageLock.Release();
        }
    }

    public async Task<Result<bool>> SaveAsync(
        CollectionArchiveSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Validate(snapshot) is { } validation)
        {
            return Result<bool>.Failure(validation);
        }

        await _storageLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = StoragePath + ".new";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, snapshot, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, StoragePath, overwrite: true);
            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            return Failure<bool>("Operation.Cancelled", "Archive profile saving was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return Failure<bool>(
                "Archive.SaveFailed",
                "TubeForge could not save archive profiles.",
                exception.GetType().Name);
        }
        finally
        {
            _storageLock.Release();
        }
    }

    internal static TubeForgeError? Validate(CollectionArchiveSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != CollectionArchiveSnapshot.CurrentSchemaVersion ||
            snapshot.Profiles is null || snapshot.Profiles.Count > MaximumProfiles)
        {
            return Invalid("The archive profile schema or profile count is invalid.");
        }

        var profileIds = new HashSet<Guid>();
        var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in snapshot.Profiles)
        {
            if (profile is null || profile.Id == Guid.Empty || !profileIds.Add(profile.Id) ||
                string.IsNullOrWhiteSpace(profile.DisplayName) || profile.DisplayName.Length > 300 ||
                string.IsNullOrWhiteSpace(profile.DestinationPath) || profile.DestinationPath.Length > 1_024 ||
                !Path.IsPathFullyQualified(profile.DestinationPath) ||
                string.IsNullOrWhiteSpace(profile.FileNameTemplate) || profile.FileNameTemplate.Length > 500 ||
                !Enum.IsDefined(profile.OutputPreset) || !Enum.IsDefined(profile.CaptionPreference) ||
                profile.LastCheckedVideoIds is null || profile.LastCheckedVideoIds.Count > MaximumCheckedItems ||
                profile.CreatedAtUtc == default || profile.LastCheckedAtUtc == default ||
                profile.CreatedAtUtc > profile.LastCheckedAtUtc ||
                profile.CreatedAtUtc > DateTimeOffset.UtcNow.AddDays(1) ||
                profile.LastCheckedAtUtc > DateTimeOffset.UtcNow.AddDays(1))
            {
                return Invalid("An archive profile contains invalid values.");
            }

            var source = YouTubeCollectionUrlParser.Parse(profile.SourceUrl);
            if (!source.IsSuccess || source.Value.Kind != profile.SourceKind ||
                !source.Value.CanonicalUrl.AbsoluteUri.Equals(profile.SourceUrl, StringComparison.Ordinal) ||
                !sources.Add(profile.SourceUrl))
            {
                return Invalid("An archive profile contains an invalid or duplicate source.");
            }

            var template = FileNameTemplate.Render(profile.FileNameTemplate, new FileNameTemplateContext
            {
                Title = "Title",
                Channel = "Channel",
                VideoId = "Fixture123_",
                Quality = "1080p",
                Container = "mp4",
                Index = 1,
                IndexWidth = 2
            });
            if (!template.IsSuccess || profile.LastCheckedVideoIds.Any(id => !IsVideoId(id)) ||
                profile.LastCheckedVideoIds.Distinct(StringComparer.Ordinal).Count() != profile.LastCheckedVideoIds.Count)
            {
                return Invalid("An archive profile contains an invalid template or checked-item set.");
            }
        }

        return null;
    }

    private static bool IsVideoId(string value) =>
        value.Length == 11 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static TubeForgeError Invalid(string detail) => new(
        "Archive.InvalidState",
        "The saved archive profiles are invalid and were left unchanged.",
        detail);

    private static Result<CollectionArchiveSnapshot> Corrupt() =>
        Failure<CollectionArchiveSnapshot>(
            "Archive.Corrupt",
            "The saved archive profiles are unreadable and were left unchanged.");

    private static Result<T> Failure<T>(string code, string message, string? detail = null) =>
        Result<T>.Failure(new TubeForgeError(code, message, detail));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
