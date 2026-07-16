using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;

namespace TubeForge.Downloads.Queue;

public sealed class DownloadQueueStore
{
    private const int MaximumItems = 10_000;
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public DownloadQueueStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public async Task<Result<DownloadQueueSnapshot>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var lockTaken = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            if (!File.Exists(_path))
            {
                return Result<DownloadQueueSnapshot>.Success(new DownloadQueueSnapshot());
            }

            var information = new FileInfo(_path);
            if (information.Length > MaximumFileBytes)
            {
                return Failure<DownloadQueueSnapshot>(
                    "Queue.Corrupt",
                    "The saved download queue exceeds the safe size limit.");
            }

            await using var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<DownloadQueueSnapshot>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Corrupt();
            }

            var validation = Validate(snapshot);
            if (validation is not null)
            {
                return Result<DownloadQueueSnapshot>.Failure(validation);
            }

            var recoveredAt = DateTimeOffset.UtcNow;
            var recovered = snapshot with
            {
                Items = snapshot.Items
                    .Select(item => item.Status == DownloadQueueStatus.Downloading
                        ? item with
                        {
                            Status = DownloadQueueStatus.Paused,
                            UpdatedAtUtc = recoveredAt
                        }
                        : item)
                    .ToArray()
            };
            return Result<DownloadQueueSnapshot>.Success(recovered);
        }
        catch (FileNotFoundException)
        {
            return Result<DownloadQueueSnapshot>.Success(new DownloadQueueSnapshot());
        }
        catch (DirectoryNotFoundException)
        {
            return Result<DownloadQueueSnapshot>.Success(new DownloadQueueSnapshot());
        }
        catch (JsonException)
        {
            return Corrupt();
        }
        catch (OperationCanceledException)
        {
            return Cancelled<DownloadQueueSnapshot>();
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure<DownloadQueueSnapshot>(
                "Queue.ReadFailed",
                "TubeForge cannot read the saved download queue.",
                exception.GetType().Name);
        }
        catch (IOException exception)
        {
            return Failure<DownloadQueueSnapshot>(
                "Queue.ReadFailed",
                "TubeForge cannot read the saved download queue.",
                exception.GetType().Name);
        }
        finally
        {
            if (lockTaken)
            {
                _gate.Release();
            }
        }
    }

    public async Task<Result<bool>> SaveAsync(
        DownloadQueueSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var validation = Validate(snapshot);
        if (validation is not null)
        {
            return Result<bool>.Failure(validation);
        }

        var lockTaken = false;
        var temporaryPath = _path + ".new";
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Failure<bool>("Queue.InvalidPath", "The queue storage path is invalid.");
            }

            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    snapshot,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporaryPath);
            return Cancelled<bool>();
        }
        catch (UnauthorizedAccessException exception)
        {
            TryDelete(temporaryPath);
            return Failure<bool>(
                "Queue.WriteFailed",
                "TubeForge cannot save the download queue.",
                exception.GetType().Name);
        }
        catch (IOException exception)
        {
            TryDelete(temporaryPath);
            return Failure<bool>(
                "Queue.WriteFailed",
                "TubeForge cannot save the download queue.",
                exception.GetType().Name);
        }
        finally
        {
            if (lockTaken)
            {
                _gate.Release();
            }
        }
    }

    private static TubeForgeError? Validate(DownloadQueueSnapshot snapshot)
    {
        if (snapshot.SchemaVersion != DownloadQueueSnapshot.CurrentSchemaVersion)
        {
            return new TubeForgeError(
                "Queue.UnsupportedSchema",
                "The saved download queue uses an unsupported schema version.");
        }

        if (snapshot.Items is null || snapshot.Items.Count > MaximumItems)
        {
            return InvalidState("The queue item collection is invalid.");
        }

        var ids = new HashSet<Guid>();
        foreach (var item in snapshot.Items)
        {
            if (item is null ||
                item.Id == Guid.Empty ||
                !ids.Add(item.Id) ||
                !YouTubeVideoId.TryCreate(item.VideoId, out _) ||
                item.FormatId <= 0 ||
                !IsSafeText(item.SourceIdentity, 256) ||
                !IsSafeText(item.DisplayTitle, 512) ||
                !IsSafeDestination(item.DestinationPath) ||
                item.ExpectedLength is <= 0 ||
                item.BytesReceived < 0 ||
                item.ExpectedLength is not null && item.BytesReceived > item.ExpectedLength ||
                !Enum.IsDefined(item.Status) ||
                item.CreatedAtUtc == default ||
                item.UpdatedAtUtc < item.CreatedAtUtc ||
                !IsSafeFailureCode(item.FailureCode))
            {
                return InvalidState("One or more queue items are invalid.");
            }
        }

        return null;
    }

    private static bool IsSafeText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        !value.Any(char.IsControl);

    private static bool IsSafeDestination(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32_767 || value.Contains('\0'))
        {
            return false;
        }

        try
        {
            return Path.IsPathFullyQualified(value) && Path.GetFullPath(value) == value;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsSafeFailureCode(string? value) =>
        value is null ||
        value.Length is > 0 and <= 128 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');

    private static TubeForgeError InvalidState(string detail) => new(
        "Queue.InvalidState",
        "The download queue contains invalid state.",
        detail);

    private static Result<DownloadQueueSnapshot> Corrupt() =>
        Failure<DownloadQueueSnapshot>(
            "Queue.Corrupt",
            "The saved download queue is malformed and was left unchanged.");

    private static Result<T> Cancelled<T>() =>
        Failure<T>("Operation.Cancelled", "The queue operation was cancelled.");

    private static Result<T> Failure<T>(string code, string message, string? detail = null) =>
        Result<T>.Failure(new TubeForgeError(code, message, detail));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
