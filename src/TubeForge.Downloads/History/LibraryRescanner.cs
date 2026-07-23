using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Downloads.History;

public sealed record LibraryRescanReceipt(
    DownloadHistorySnapshot Snapshot,
    int FilesScanned,
    int RecordsRepaired,
    int AmbiguousMatches);

public static class LibraryRescanner
{
    public const int MaximumFiles = 100_000;

    public static Result<LibraryRescanReceipt> Rescan(
        DownloadHistorySnapshot snapshot,
        string rootPath)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (DownloadHistoryStore.ValidateSnapshot(snapshot) is { } validation)
        {
            return Result<LibraryRescanReceipt>.Failure(validation);
        }

        string root;
        try
        {
            root = Path.GetFullPath(rootPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure("Library.InvalidRescanRoot", "Choose a valid folder to rescan.");
        }

        if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root))
        {
            return Failure("Library.InvalidRescanRoot", "Choose an existing folder to rescan.");
        }

        try
        {
            var files = new List<FileCandidate>();
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false
            };
            foreach (var path in Directory.EnumerateFiles(root, "*", options))
            {
                if (files.Count >= MaximumFiles)
                {
                    return Failure(
                        "Library.RescanTooLarge",
                        "The selected folder contains more files than the bounded Library rescan allows.");
                }

                var information = new FileInfo(path);
                if (information.Length > 0)
                {
                    files.Add(new FileCandidate(
                        information.Name,
                        information.Length,
                        information.FullName));
                }
            }

            var byNameAndSize = files
                .GroupBy(file => (file.Name, file.Length), new FileIdentityComparer())
                .ToDictionary(group => group.Key, group => group.ToArray(), new FileIdentityComparer());
            var repaired = 0;
            var ambiguous = 0;
            var entries = snapshot.Entries.Select(entry =>
            {
                if (File.Exists(entry.DestinationPath))
                {
                    return entry;
                }

                var key = (Path.GetFileName(entry.DestinationPath), entry.BytesWritten);
                if (!byNameAndSize.TryGetValue(key, out var matches))
                {
                    return entry;
                }

                if (matches.Length != 1)
                {
                    ambiguous++;
                    return entry;
                }

                repaired++;
                return entry with { DestinationPath = matches[0].FullPath };
            }).ToArray();
            return Result<LibraryRescanReceipt>.Success(new LibraryRescanReceipt(
                new DownloadHistorySnapshot { Entries = entries },
                files.Count,
                repaired,
                ambiguous));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "Library.RescanFailed",
                "TubeForge could not safely scan the selected folder.",
                exception.GetType().Name);
        }
    }

    private static Result<LibraryRescanReceipt> Failure(
        string code,
        string message,
        string? detail = null) =>
        Result<LibraryRescanReceipt>.Failure(new TubeForgeError(code, message, detail));

    private sealed record FileCandidate(string Name, long Length, string FullPath);

    private sealed class FileIdentityComparer : IEqualityComparer<(string Name, long Length)>
    {
        public bool Equals((string Name, long Length) x, (string Name, long Length) y) =>
            x.Length == y.Length && x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Name, long Length) value) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Name), value.Length);
    }
}
