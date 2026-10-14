using System.Text.RegularExpressions;

namespace TubeForge.Downloads.Queue;

/// <summary>
/// The partial and temporary files a queue item leaves beside its destination, and
/// only those.
/// <para>
/// A download that failed or was cancelled can leave gigabytes behind: the
/// <c>.part</c> file and its resume state, intermediate audio and video tracks, a
/// pre-trim or pre-caption source, a live capture's segment folder, and FFmpeg
/// scratch files. Each of them is named <c>&lt;destination file name&gt;.&lt;suffix&gt;</c>
/// with a suffix the app itself chose, so ownership is decided by an exact match on
/// that name, in the destination's own folder. The destination file itself, any
/// other file, a link, and a folder holding anything unexpected are never touched.
/// </para>
/// </summary>
public static partial class QueueItemArtifacts
{
    private const string Extension = @"[A-Za-z0-9]{1,10}";

    // Files the job writes under its own name: intermediate tracks, pre-processing
    // sources, the live capture and its journal, and caption sidecars.
    private const string Core =
        @"(?:(?:video-track|audio-track|source|chapter-source|caption-source|trim-source)\." + Extension +
        @"|hls-source|hls-source\.hls\.json(?:\.new|\.bak)?|caption-\d{1,3}-source\.srt)";

    // What the transfer engines and FFmpeg add to any of those (or to the
    // destination): partial data, resume state and per-run scratch files.
    private const string Suffix =
        @"(?:part|part\.json|part\.segments\.json" +
        @"|[0-9a-f]{32}\.(?:processing\." + Extension + @"|transcoding\." + Extension + @"|chapters\.ffmetadata))";

    private const string LivePartsSuffix = ".hls-source.hls.parts";

    /// <summary>
    /// The files and the live-capture segment folder that belong to the item whose
    /// final output is <paramref name="destinationPath"/>. Nothing is deleted.
    /// </summary>
    public static IReadOnlyList<string> Find(string destinationPath)
    {
        if (!TryResolve(destinationPath, out var directory, out var fileName))
        {
            return [];
        }

        var owned = new List<string>();
        var prefix = fileName + ".";
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos(fileName + ".*"))
        {
            if (!entry.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var suffix = entry.Name[prefix.Length..];
            if (entry is FileInfo && OwnedFile().IsMatch(suffix))
            {
                owned.Add(entry.FullName);
            }
            else if (entry is DirectoryInfo folder &&
                string.Equals("." + suffix, LivePartsSuffix, StringComparison.OrdinalIgnoreCase) &&
                HoldsOnlyLiveSegments(folder))
            {
                owned.Add(folder.FullName);
            }
        }

        return owned;
    }

    /// <summary>
    /// Deletes what <see cref="Find"/> reports. Best effort: a file that is locked or
    /// already gone is reported, never retried, and never stops the rest.
    /// </summary>
    public static QueueItemArtifactCleanup Delete(string destinationPath)
    {
        var deleted = 0;
        var failed = new List<string>();
        foreach (var path in Find(destinationPath))
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var folder = new DirectoryInfo(path);
                    // Checked again right before deleting: the folder may have changed
                    // since it was found.
                    if (folder.Attributes.HasFlag(FileAttributes.ReparsePoint) || !HoldsOnlyLiveSegments(folder))
                    {
                        failed.Add(path);
                        continue;
                    }

                    foreach (var segment in folder.EnumerateFiles())
                    {
                        segment.Delete();
                    }

                    folder.Delete(recursive: false);
                }
                else
                {
                    File.Delete(path);
                }

                deleted++;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failed.Add(path);
            }
        }

        return new QueueItemArtifactCleanup(deleted, failed);
    }

    private static bool TryResolve(string destinationPath, out string directory, out string fileName)
    {
        directory = string.Empty;
        fileName = string.Empty;
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(destinationPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        fileName = Path.GetFileName(fullPath);
        return directory.Length > 0 &&
            fileName.Length > 0 &&
            fileName.IndexOfAny(['*', '?']) < 0 &&
            Directory.Exists(directory);
    }

    private static bool HoldsOnlyLiveSegments(DirectoryInfo folder)
    {
        foreach (var entry in folder.EnumerateFileSystemInfos())
        {
            if (entry is not FileInfo ||
                entry.Attributes.HasFlag(FileAttributes.ReparsePoint) ||
                !LiveSegment().IsMatch(entry.Name))
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(
        "^(?:" + Core + "|(?:" + Core + @"\.)?" + Suffix + ")$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex OwnedFile();

    [GeneratedRegex(@"^\d{20}\.bin(?:\.new)?$", RegexOptions.CultureInvariant)]
    private static partial Regex LiveSegment();
}

/// <param name="Deleted">Files and folders removed.</param>
/// <param name="Failed">Paths that could not be removed and were left in place.</param>
public sealed record QueueItemArtifactCleanup(int Deleted, IReadOnlyList<string> Failed);
