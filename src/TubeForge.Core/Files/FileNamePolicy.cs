using System.Globalization;
using System.Text;

namespace TubeForge.Core.Files;

public static class FileNamePolicy
{
    public const int DefaultMaximumStemLength = 160;

    private static readonly HashSet<char> InvalidCharacters =
        ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string SanitizeStem(string? value, int maximumLength = DefaultMaximumStemLength)
    {
        if (maximumLength < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        }

        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormC);
        var output = new StringBuilder(Math.Min(normalized.Length, maximumLength));
        var pendingSpace = false;
        Span<char> runeBuffer = stackalloc char[2];

        foreach (var rune in normalized.EnumerateRunes())
        {
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control ||
                (rune.IsAscii && InvalidCharacters.Contains((char)rune.Value)))
            {
                pendingSpace = output.Length > 0;
                continue;
            }

            if (Rune.IsWhiteSpace(rune))
            {
                pendingSpace = output.Length > 0;
                continue;
            }

            var encodedLength = rune.Utf16SequenceLength;
            if (output.Length + (pendingSpace ? 1 : 0) + encodedLength > maximumLength)
            {
                break;
            }

            if (pendingSpace)
            {
                output.Append(' ');
                pendingSpace = false;
            }

            var charsWritten = rune.EncodeToUtf16(runeBuffer);
            output.Append(runeBuffer[..charsWritten]);
        }

        var stem = output.ToString().Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "video";
        }

        if (ReservedNames.Contains(stem) || ReservedNames.Contains(stem.Split('.')[0]))
        {
            stem = "_" + stem;
        }

        return stem;
    }

    public static string AvailablePath(
        string directory,
        string unsafeStem,
        string extension,
        Func<string, bool>? pathExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        pathExists ??= File.Exists;

        var safeStem = SanitizeStem(unsafeStem);
        var safeExtension = NormalizeExtension(extension);
        var directoryPath = Path.GetFullPath(directory);

        for (var suffix = 0; suffix < 10_000; suffix++)
        {
            var name = suffix == 0 ? safeStem : $"{safeStem} ({suffix})";
            var candidate = Path.GetFullPath(Path.Combine(directoryPath, name + safeExtension));
            if (!IsDirectChild(directoryPath, candidate))
            {
                throw new InvalidOperationException("Generated output path escaped its target directory.");
            }

            if (!pathExists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Could not find an available output filename.");
    }

    private static string NormalizeExtension(string extension)
    {
        var candidate = extension.Trim();
        if (!candidate.StartsWith('.'))
        {
            candidate = "." + candidate;
        }

        if (candidate.Length is < 2 or > 10 ||
            candidate.Skip(1).Any(character => !char.IsAsciiLetterOrDigit(character)))
        {
            throw new ArgumentException("The extension is invalid.", nameof(extension));
        }

        return candidate.ToLowerInvariant();
    }

    private static bool IsDirectChild(string directory, string candidate) =>
        Path.GetDirectoryName(candidate)?.TrimEnd(Path.DirectorySeparatorChar)
            .Equals(directory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) == true;
}
