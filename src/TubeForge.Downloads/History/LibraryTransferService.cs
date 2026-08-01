using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Downloads.History;

public sealed record LibraryTransferDocument
{
    public const int CurrentSchemaVersion = 2;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public DateTimeOffset ExportedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<DownloadHistoryEntry> Entries { get; init; } = [];
}

public sealed class LibraryTransferService
{
    private const long MaximumFileBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 24,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<Result<bool>> ExportAsync(
        DownloadHistorySnapshot snapshot,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (DownloadHistoryStore.ValidateSnapshot(snapshot) is { } validation)
        {
            return Result<bool>.Failure(validation);
        }

        if (!TryGetSafePath(path, out var fullPath))
        {
            return Failure<bool>("Library.InvalidExportPath", "Choose a valid Library export file.");
        }

        var temporary = fullPath + ".new";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await using (var stream = new FileStream(
                             temporary,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    new LibraryTransferDocument { Entries = snapshot.Entries },
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, fullPath, overwrite: true);
            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            TryDelete(temporary);
            return Failure<bool>("Operation.Cancelled", "Library export was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            return Failure<bool>(
                "Library.ExportFailed",
                "TubeForge could not export the Library.",
                exception.GetType().Name);
        }
    }

    public async Task<Result<DownloadHistorySnapshot>> ImportAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetSafePath(path, out var fullPath) || !File.Exists(fullPath))
        {
            return Failure<DownloadHistorySnapshot>(
                "Library.InvalidImportPath",
                "Choose an existing Library export file.");
        }

        try
        {
            if (new FileInfo(fullPath).Length > MaximumFileBytes)
            {
                return InvalidImport("The Library import exceeds its safe size limit.");
            }

            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<LibraryTransferDocument>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            if (document is null || document.SchemaVersion is not (1 or 2) ||
                document.Entries is null || document.Entries.Count > DownloadHistoryStore.MaximumEntries ||
                document.SchemaVersion == 2 &&
                    (document.ExportedAtUtc == default ||
                     document.ExportedAtUtc > DateTimeOffset.UtcNow.AddDays(1)))
            {
                return InvalidImport("The Library import schema or metadata is invalid.");
            }

            var snapshot = new DownloadHistorySnapshot { Entries = document.Entries };
            return DownloadHistoryStore.ValidateSnapshot(snapshot) is { } validation
                ? Result<DownloadHistorySnapshot>.Failure(validation with
                {
                    Code = "Library.InvalidImport",
                    Message = "The Library import contains invalid records."
                })
                : Result<DownloadHistorySnapshot>.Success(snapshot);
        }
        catch (JsonException)
        {
            return InvalidImport("The Library import is malformed.");
        }
        catch (OperationCanceledException)
        {
            return Failure<DownloadHistorySnapshot>("Operation.Cancelled", "Library import was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure<DownloadHistorySnapshot>(
                "Library.ImportFailed",
                "TubeForge could not read the Library import.",
                exception.GetType().Name);
        }
    }

    public static Result<DownloadHistorySnapshot> Merge(
        DownloadHistorySnapshot current,
        DownloadHistorySnapshot imported)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(imported);
        var currentError = DownloadHistoryStore.ValidateSnapshot(current);
        var importedError = DownloadHistoryStore.ValidateSnapshot(imported);
        if (currentError is not null || importedError is not null)
        {
            return Result<DownloadHistorySnapshot>.Failure(currentError ?? importedError!);
        }

        var entries = current.Entries
            .Concat(imported.Entries)
            .GroupBy(
                entry => (entry.SourceIdentity, entry.DestinationPath),
                new HistoryIdentityComparer())
            .Select(group => group
                .OrderByDescending(entry => entry.CompletedAtUtc)
                .ThenByDescending(entry => entry.BytesWritten)
                .First())
            .OrderByDescending(entry => entry.CompletedAtUtc)
            .Take(DownloadHistoryStore.MaximumEntries)
            .ToArray();
        var usedIds = new HashSet<Guid>();
        entries = entries.Select(entry => usedIds.Add(entry.Id)
            ? entry
            : entry with { Id = Guid.NewGuid() }).ToArray();
        return Result<DownloadHistorySnapshot>.Success(new DownloadHistorySnapshot { Entries = entries });
    }

    private static bool TryGetSafePath(string? path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
            return Path.IsPathFullyQualified(fullPath) &&
                   !string.IsNullOrWhiteSpace(Path.GetDirectoryName(fullPath));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static Result<DownloadHistorySnapshot> InvalidImport(string detail) =>
        Failure<DownloadHistorySnapshot>(
            "Library.InvalidImport",
            "The Library import is invalid and was left unchanged.",
            detail);

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

    private sealed class HistoryIdentityComparer : IEqualityComparer<(string SourceIdentity, string DestinationPath)>
    {
        public bool Equals(
            (string SourceIdentity, string DestinationPath) x,
            (string SourceIdentity, string DestinationPath) y) =>
            x.SourceIdentity.Equals(y.SourceIdentity, StringComparison.Ordinal) &&
            x.DestinationPath.Equals(y.DestinationPath, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string SourceIdentity, string DestinationPath) value) =>
            HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.SourceIdentity),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.DestinationPath));
    }
}
