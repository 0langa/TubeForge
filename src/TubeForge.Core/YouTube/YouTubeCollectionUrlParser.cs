using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Core.YouTube;

public static class YouTubeCollectionUrlParser
{
    private static readonly HashSet<string> StandardHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "youtube.com",
        "www.youtube.com",
        "m.youtube.com",
        "music.youtube.com"
    };

    public static Result<YouTubeCollectionReference> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Failure("Enter a YouTube playlist or channel URL.");
        }

        var candidate = input.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
        {
            candidate = "https://" + candidate;
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(uri.UserInfo))
        {
            return Failure("The value is not a valid YouTube HTTP or HTTPS URL.");
        }

        if (StandardHosts.Contains(uri.Host) || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            var playlistId = QueryValue(uri.Query, "list");
            if (IsSafePlaylistId(playlistId) &&
                (uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) || IsPlaylistRoute(uri)))
            {
                return Result<YouTubeCollectionReference>.Success(new YouTubeCollectionReference
                {
                    Kind = YouTubeCollectionKind.Playlist,
                    Identifier = playlistId!,
                    CanonicalUrl = new Uri(
                        $"https://www.youtube.com/playlist?list={Uri.EscapeDataString(playlistId!)}")
                });
            }
        }

        if (!StandardHosts.Contains(uri.Host))
        {
            return Failure("Only official YouTube playlist and channel URLs are supported.", unsupported: true);
        }

        var segments = PathSegments(uri);
        if (segments.Length is 1 or 2 &&
            segments[0].StartsWith('@') &&
            IsSafeHandle(segments[0][1..]) &&
            (segments.Length == 1 || IsSupportedChannelTab(segments[1])))
        {
            var handleSegment = segments[0];
            return Channel(handleSegment, $"/{Uri.EscapeDataString(handleSegment)}/videos");
        }

        if (segments is [var route, var identifier, ..] &&
            ((route.Equals("channel", StringComparison.OrdinalIgnoreCase) && IsSafeChannelId(identifier)) ||
             ((route.Equals("c", StringComparison.OrdinalIgnoreCase) ||
               route.Equals("user", StringComparison.OrdinalIgnoreCase)) && IsSafeLegacyName(identifier))))
        {
            var normalizedRoute = route.ToLowerInvariant();
            return Channel(identifier, $"/{normalizedRoute}/{Uri.EscapeDataString(identifier)}/videos");
        }

        return Failure("The URL does not contain a supported playlist or channel identifier.");
    }

    private static Result<YouTubeCollectionReference> Channel(string identifier, string path) =>
        Result<YouTubeCollectionReference>.Success(new YouTubeCollectionReference
        {
            Kind = YouTubeCollectionKind.Channel,
            Identifier = identifier,
            CanonicalUrl = new Uri("https://www.youtube.com" + path)
        });

    private static bool IsPlaylistRoute(Uri uri)
    {
        var segments = PathSegments(uri);
        return segments.Length == 1 &&
               (segments[0].Equals("playlist", StringComparison.OrdinalIgnoreCase) ||
                segments[0].Equals("watch", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSupportedChannelTab(string value) =>
        value.Equals("videos", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("shorts", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("streams", StringComparison.OrdinalIgnoreCase);

    private static bool IsSafePlaylistId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length is >= 10 and <= 64 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool IsSafeChannelId(string value) =>
        value.Length == 24 &&
        value.StartsWith("UC", StringComparison.Ordinal) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool IsSafeHandle(string value) =>
        value.Length is >= 3 and <= 30 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');

    private static bool IsSafeLegacyName(string value) =>
        value.Length is >= 1 and <= 100 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '.' or '_' or '-');

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

    private static Result<YouTubeCollectionReference> Failure(string message, bool unsupported = false) =>
        Result<YouTubeCollectionReference>.Failure(unsupported
            ? TubeForgeError.UnsupportedUrl(message)
            : TubeForgeError.InvalidUrl(message));
}
