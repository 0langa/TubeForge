using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Core.YouTube;

public static class YouTubeUrlParser
{
    private static readonly HashSet<string> StandardHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com"
    };

    private static readonly HashSet<string> EmbedOnlyHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube-nocookie.com",
        "www.youtube-nocookie.com"
    };

    public static Result<YouTubeVideoId> ParseVideoId(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Result<YouTubeVideoId>.Failure(
                TubeForgeError.InvalidUrl("Enter a YouTube video URL."));
        }

        var trimmed = input.Trim();
        if (!trimmed.Contains("://", StringComparison.Ordinal))
        {
            trimmed = "https://" + trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return Result<YouTubeVideoId>.Failure(
                TubeForgeError.InvalidUrl("The value is not a valid HTTP or HTTPS URL."));
        }

        string? candidate;
        if (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            candidate = FirstPathSegment(uri);
        }
        else if (StandardHosts.Contains(uri.Host))
        {
            candidate = CandidateFromStandardUrl(uri);
        }
        else if (EmbedOnlyHosts.Contains(uri.Host) &&
                 PathSegments(uri) is [var route, var id, ..] &&
                 route.Equals("embed", StringComparison.OrdinalIgnoreCase))
        {
            candidate = id;
        }
        else
        {
            return Result<YouTubeVideoId>.Failure(
                TubeForgeError.UnsupportedUrl("Only official YouTube video URLs are supported."));
        }

        if (!YouTubeVideoId.TryCreate(candidate, out var videoId))
        {
            return Result<YouTubeVideoId>.Failure(
                TubeForgeError.InvalidUrl("The URL does not contain a valid YouTube video ID."));
        }

        return Result<YouTubeVideoId>.Success(videoId);
    }

    private static string? CandidateFromStandardUrl(Uri uri)
    {
        var segments = PathSegments(uri);
        if (segments.Length == 1 && segments[0].Equals("watch", StringComparison.OrdinalIgnoreCase))
        {
            return QueryValue(uri.Query, "v");
        }

        if (segments is [var route, var id, ..] &&
            (route.Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
             route.Equals("live", StringComparison.OrdinalIgnoreCase) ||
             route.Equals("embed", StringComparison.OrdinalIgnoreCase)))
        {
            return id;
        }

        return null;
    }

    private static string? FirstPathSegment(Uri uri)
    {
        var segments = PathSegments(uri);
        return segments.Length > 0 ? segments[0] : null;
    }

    private static string[] PathSegments(Uri uri) => uri.AbsolutePath
        .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(Uri.UnescapeDataString)
        .ToArray();

    private static string? QueryValue(string query, string key)
    {
        foreach (var component in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equalsIndex = component.IndexOf('=');
            var encodedKey = equalsIndex < 0 ? component : component[..equalsIndex];
            if (!Uri.UnescapeDataString(encodedKey.Replace('+', ' '))
                    .Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var encodedValue = equalsIndex < 0 ? string.Empty : component[(equalsIndex + 1)..];
            return Uri.UnescapeDataString(encodedValue.Replace('+', ' '));
        }

        return null;
    }
}
