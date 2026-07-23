using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;
using TubeForge.Downloads.Queue;

namespace TubeForge.Downloads.History;

public sealed class DownloadHistoryStore
{
    public const int MaximumEntries = 10_000;
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 24,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _path;

    public DownloadHistoryStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
    }

    public string StoragePath => _path;

    public async Task<Result<DownloadHistorySnapshot>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        var lockTaken = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            Result<DownloadHistorySnapshot>? firstFailure = null;
            foreach (var candidatePath in new[] { _path, PendingPath, BackupPath })
            {
                var candidate = await TryLoadCandidateAsync(candidatePath, cancellationToken).ConfigureAwait(false);
                if (candidate is not { } candidateResult)
                {
                    continue;
                }

                if (candidateResult.IsSuccess)
                {
                    return candidateResult;
                }

                if (candidateResult.Error?.Code == "Operation.Cancelled")
                {
                    return candidateResult;
                }

                firstFailure ??= candidateResult;
            }

            return firstFailure ?? Result<DownloadHistorySnapshot>.Success(new DownloadHistorySnapshot());
        }
        catch (OperationCanceledException)
        {
            return Cancelled<DownloadHistorySnapshot>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure<DownloadHistorySnapshot>(
                "History.ReadFailed",
                "TubeForge cannot read local download history.",
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
        DownloadHistorySnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var validation = ValidateSnapshot(snapshot);
        if (validation is not null)
        {
            return Result<bool>.Failure(validation);
        }

        var lockTaken = false;
        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            lockTaken = true;
            var directory = Path.GetDirectoryName(_path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Failure<bool>("History.InvalidPath", "The history storage path is invalid.");
            }

            Directory.CreateDirectory(directory);
            await using (var stream = new FileStream(
                             PendingPath,
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
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_path))
            {
                TryDelete(BackupPath);
                File.Replace(PendingPath, _path, BackupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(PendingPath, _path);
            }

            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(PendingPath);
            return Cancelled<bool>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(PendingPath);
            return Failure<bool>(
                "History.WriteFailed",
                "TubeForge cannot save local download history.",
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

    private string PendingPath => _path + ".new";

    private string BackupPath => _path + ".bak";

    private static async Task<Result<DownloadHistorySnapshot>?> TryLoadCandidateAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if (new FileInfo(path).Length > MaximumFileBytes)
            {
                return Corrupt();
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var snapshot = await JsonSerializer.DeserializeAsync<DownloadHistorySnapshot>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                return Corrupt();
            }

            var validation = ValidateSnapshot(snapshot);
            return validation is null
                ? Result<DownloadHistorySnapshot>.Success(snapshot)
                : Result<DownloadHistorySnapshot>.Failure(validation);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return Corrupt();
        }
        catch (OperationCanceledException)
        {
            return Cancelled<DownloadHistorySnapshot>();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure<DownloadHistorySnapshot>(
                "History.ReadFailed",
                "TubeForge cannot read local download history.",
                exception.GetType().Name);
        }
    }

    internal static TubeForgeError? ValidateSnapshot(DownloadHistorySnapshot snapshot)
    {
        if (snapshot.SchemaVersion != DownloadHistorySnapshot.CurrentSchemaVersion)
        {
            return new TubeForgeError(
                "History.UnsupportedSchema",
                "The local download history uses an unsupported schema version.");
        }

        if (snapshot.Entries is null || snapshot.Entries.Count > MaximumEntries)
        {
            return InvalidState();
        }

        var ids = new HashSet<Guid>();
        foreach (var entry in snapshot.Entries)
        {
            if (entry is null ||
                entry.Id == Guid.Empty ||
                !ids.Add(entry.Id) ||
                !YouTubeVideoId.TryCreate(entry.VideoId, out var videoId) ||
                !IsSafeText(entry.SourceIdentity, 256) ||
                !DownloadSourceIdentity.TryParse(entry.SourceIdentity, out var identity) ||
                identity.VideoId != videoId ||
                !IsSafeText(entry.DisplayTitle, 512) ||
                !IsSafeDestination(entry.DestinationPath) ||
                entry.BytesWritten <= 0 ||
                entry.CompletedAtUtc == default ||
                entry.CompletedAtUtc > DateTimeOffset.UtcNow.AddDays(1))
            {
                return InvalidState();
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

    private static TubeForgeError InvalidState() => new(
        "History.InvalidState",
        "The local download history contains invalid state.");

    private static Result<DownloadHistorySnapshot> Corrupt() =>
        Failure<DownloadHistorySnapshot>(
            "History.Corrupt",
            "The local download history is malformed and was left unchanged.");

    private static Result<T> Cancelled<T>() =>
        Failure<T>("Operation.Cancelled", "The history operation was cancelled.");

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
