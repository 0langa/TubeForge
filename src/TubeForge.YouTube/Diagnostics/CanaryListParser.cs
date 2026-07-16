using TubeForge.Core.Errors;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;

namespace TubeForge.YouTube.Diagnostics;

public static class CanaryListParser
{
    public const int MaximumCanaries = 25;

    public static Result<IReadOnlyList<YouTubeVideoId>> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var videoIds = new List<YouTubeVideoId>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in lines)
        {
            var line = rawLine?.Trim() ?? string.Empty;
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (videoIds.Count >= MaximumCanaries)
            {
                return Failure(
                    "Canary.TooManyItems",
                    $"The canary list cannot contain more than {MaximumCanaries} URLs.");
            }

            var parsed = YouTubeUrlParser.ParseVideoId(line);
            if (!parsed.IsSuccess)
            {
                return Failure("Canary.InvalidUrl", "The canary list contains an invalid YouTube URL.");
            }

            if (!seen.Add(parsed.Value.Value))
            {
                return Failure("Canary.Duplicate", "The canary list contains a duplicate video.");
            }

            videoIds.Add(parsed.Value);
        }

        return videoIds.Count == 0
            ? Failure("Canary.Empty", "The canary list contains no URLs.")
            : Result<IReadOnlyList<YouTubeVideoId>>.Success(videoIds);
    }

    private static Result<IReadOnlyList<YouTubeVideoId>> Failure(string code, string message) =>
        Result<IReadOnlyList<YouTubeVideoId>>.Failure(new TubeForgeError(code, message));
}
