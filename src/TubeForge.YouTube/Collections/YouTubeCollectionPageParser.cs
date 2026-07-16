using System.Globalization;
using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;

namespace TubeForge.YouTube.Collections;

public static class YouTubeCollectionPageParser
{
    private const int MaximumItemsPerPage = 5_000;
    private const int MaximumVisitedNodes = 250_000;
    private const string InitialDataMarker = "ytInitialData";

    public static Result<YouTubeCollectionPage> ParseInitialHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Failure("The collection page was empty.");
        }

        var searchIndex = 0;
        while (TryFindNextJsonObject(html, InitialDataMarker, searchIndex, out var json, out var nextIndex))
        {
            searchIndex = nextIndex;
            var parsed = ParseJson(json, includeTitle: true);
            if (parsed.IsSuccess)
            {
                var context = ParseContinuationContext(html);
                return Result<YouTubeCollectionPage>.Success(parsed.Value with
                {
                    ContinuationContext = context
                });
            }
        }

        return Failure("The page did not contain supported YouTube collection data.");
    }

    public static Result<YouTubeCollectionPage> ParseContinuationJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Failure("The collection continuation was empty.");
        }

        return ParseJson(json, includeTitle: false);
    }

    private static Result<YouTubeCollectionPage> ParseJson(string json, bool includeTitle)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            var state = new ParseState(includeTitle);
            Visit(document.RootElement, state, depth: 0);
            if (state.Items.Count == 0 && state.ContinuationToken is null)
            {
                return Failure("The collection data contained no supported videos or continuation.");
            }

            return Result<YouTubeCollectionPage>.Success(new YouTubeCollectionPage(
                state.Title,
                state.Items
                    .DistinctBy(item => item.VideoId)
                    .Take(MaximumItemsPerPage)
                    .ToArray(),
                state.ContinuationToken,
                null));
        }
        catch (JsonException exception)
        {
            return Failure("The collection data was malformed.", exception.GetType().Name);
        }
    }

    private static void Visit(JsonElement element, ParseState state, int depth)
    {
        if (depth > 64 || state.VisitedNodes++ >= MaximumVisitedNodes)
        {
            return;
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                Visit(child, state, depth + 1);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (state.Items.Count < MaximumItemsPerPage)
        {
            if (element.TryGetProperty("playlistVideoRenderer", out var playlistVideo))
            {
                TryAddItem(playlistVideo, state, hasPlaylistIndex: true);
            }
            else if (element.TryGetProperty("richItemRenderer", out var richItem) &&
                     richItem.TryGetProperty("content", out var content) &&
                     content.TryGetProperty("videoRenderer", out var channelVideo))
            {
                TryAddItem(channelVideo, state, hasPlaylistIndex: false);
            }
            else if (element.TryGetProperty("lockupViewModel", out var lockup))
            {
                TryAddLockupItem(lockup, state);
            }
        }

        if (state.ContinuationToken is null &&
            element.TryGetProperty("continuationItemRenderer", out var continuation))
        {
            state.ContinuationToken = FindContinuationToken(continuation);
        }

        if (state.ContinuationToken is null &&
            element.TryGetProperty("continuationCommand", out var continuationCommand))
        {
            var token = FindStringProperty(continuationCommand, "token", depth: 0);
            state.ContinuationToken = IsSafeOpaqueToken(token, 4_096) ? token : null;
        }

        if (state.IncludeTitle && string.IsNullOrWhiteSpace(state.Title))
        {
            state.Title = ReadCollectionTitle(element) ?? string.Empty;
        }

        foreach (var property in element.EnumerateObject())
        {
            Visit(property.Value, state, depth + 1);
        }
    }

    private static void TryAddItem(JsonElement renderer, ParseState state, bool hasPlaylistIndex)
    {
        var rawId = ReadString(renderer, "videoId");
        var title = ReadLocalizedText(renderer, "title")?.Trim();
        if (!YouTubeVideoId.TryCreate(rawId, out var videoId) ||
            string.IsNullOrWhiteSpace(title) ||
            title.Length > 512 ||
            title.Any(char.IsControl))
        {
            return;
        }

        state.Items.Add(new YouTubeCollectionItem
        {
            VideoId = videoId,
            Title = title,
            Index = hasPlaylistIndex ? ParseIndex(ReadLocalizedText(renderer, "index")) : null,
            Duration = ParseDuration(ReadString(renderer, "lengthSeconds")),
            ThumbnailUrl = ParseThumbnail(renderer)
        });
    }

    private static void TryAddLockupItem(JsonElement lockup, ParseState state)
    {
        var rawId = ReadNestedString(lockup, "onTap", "innertubeCommand", "watchEndpoint", "videoId") ??
                    ReadString(lockup, "contentId");
        var title = ReadNestedString(lockup, "metadata", "lockupMetadataViewModel", "title", "content")?.Trim();
        if (!YouTubeVideoId.TryCreate(rawId, out var videoId) ||
            string.IsNullOrWhiteSpace(title) ||
            title.Length > 512 ||
            title.Any(char.IsControl))
        {
            return;
        }

        state.Items.Add(new YouTubeCollectionItem
        {
            VideoId = videoId,
            Title = title,
            Index = ReadInt32(lockup, "index") is > 0 ? ReadInt32(lockup, "index") : null,
            Duration = FindBadgeDuration(lockup, depth: 0),
            ThumbnailUrl = ParseLockupThumbnail(lockup)
        });
    }

    private static string? ReadCollectionTitle(JsonElement element)
    {
        foreach (var rendererName in new[]
                 {
                     "playlistSidebarPrimaryInfoRenderer",
                     "playlistMetadataRenderer",
                     "channelMetadataRenderer"
                 })
        {
            if (!element.TryGetProperty(rendererName, out var renderer))
            {
                continue;
            }

            var localized = ReadLocalizedText(renderer, "title");
            var plain = ReadString(renderer, "title");
            var title = localized ?? plain;
            if (!string.IsNullOrWhiteSpace(title) && title.Length <= 512 && !title.Any(char.IsControl))
            {
                return title.Trim();
            }
        }

        return null;
    }

    private static string? FindContinuationToken(JsonElement continuation)
    {
        if (!continuation.TryGetProperty("continuationEndpoint", out var endpoint) ||
            !endpoint.TryGetProperty("continuationCommand", out var command))
        {
            return null;
        }

        var token = ReadString(command, "token");
        return IsSafeOpaqueToken(token, 4_096) ? token : null;
    }

    private static YouTubeContinuationContext? ParseContinuationContext(string html)
    {
        var apiKey = ExtractConfigurationValue(html, "INNERTUBE_API_KEY");
        var clientVersion = ExtractConfigurationValue(html, "INNERTUBE_CLIENT_VERSION");
        var visitorData = ExtractConfigurationValue(html, "VISITOR_DATA");
        if (!IsSafeConfigurationToken(apiKey, 256) ||
            string.IsNullOrWhiteSpace(clientVersion) ||
            clientVersion.Length > 64 ||
            clientVersion.Any(character => !char.IsAsciiDigit(character) && character != '.'))
        {
            return null;
        }

        if (!IsSafeOpaqueToken(visitorData, 1_024))
        {
            visitorData = null;
        }

        return new YouTubeContinuationContext(apiKey!, clientVersion, visitorData);
    }

    private static Uri? ParseThumbnail(JsonElement renderer)
    {
        if (!renderer.TryGetProperty("thumbnail", out var thumbnail) ||
            !thumbnail.TryGetProperty("thumbnails", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Uri? selected = null;
        foreach (var candidate in thumbnails.EnumerateArray())
        {
            var raw = ReadString(candidate, "url");
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Host.Equals("ytimg.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith(".ytimg.com", StringComparison.OrdinalIgnoreCase)))
            {
                selected = uri;
            }
        }

        return selected;
    }

    private static Uri? ParseLockupThumbnail(JsonElement lockup)
    {
        if (!TryReadPath(
                lockup,
                out var sources,
                "contentImage",
                "thumbnailViewModel",
                "image",
                "sources") ||
            sources.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Uri? selected = null;
        foreach (var candidate in sources.EnumerateArray())
        {
            var raw = ReadString(candidate, "url");
            if (Uri.TryCreate(raw, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                string.IsNullOrEmpty(uri.UserInfo) &&
                (uri.Host.Equals("ytimg.com", StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith(".ytimg.com", StringComparison.OrdinalIgnoreCase)))
            {
                selected = uri;
            }
        }

        return selected;
    }

    private static TimeSpan? FindBadgeDuration(JsonElement element, int depth)
    {
        if (depth > 12)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("thumbnailBadgeViewModel", out var badge))
            {
                var parsed = ParseClockDuration(ReadString(badge, "text"));
                if (parsed is not null)
                {
                    return parsed;
                }
            }

            foreach (var property in element.EnumerateObject())
            {
                var parsed = FindBadgeDuration(property.Value, depth + 1);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var parsed = FindBadgeDuration(child, depth + 1);
                if (parsed is not null)
                {
                    return parsed;
                }
            }
        }

        return null;
    }

    private static TimeSpan? ParseClockDuration(string? value)
    {
        var parts = value?.Split(':');
        if (parts is null or { Length: < 2 or > 3 } ||
            parts.Any(part => !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
        {
            return null;
        }

        var values = parts.Select(part => int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
        var hours = values.Length == 3 ? values[0] : 0;
        var minutes = values[^2];
        var seconds = values[^1];
        if (hours < 0 || minutes is < 0 or >= 60 || seconds is < 0 or >= 60)
        {
            return null;
        }

        try
        {
            return new TimeSpan(hours, minutes, seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static int? ParseIndex(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index > 0
            ? index
            : null;

    private static TimeSpan? ParseDuration(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) &&
        seconds >= 0 &&
        seconds <= TimeSpan.MaxValue.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : null;

    private static string? ReadLocalizedText(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var text) || text.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var simpleText = ReadString(text, "simpleText");
        if (!string.IsNullOrWhiteSpace(simpleText))
        {
            return simpleText;
        }

        if (!text.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var parts = runs.EnumerateArray()
            .Select(run => ReadString(run, "text"))
            .Where(part => part is not null)
            .ToArray();
        return parts.Length == 0 ? null : string.Concat(parts);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? ReadInt32(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static string? ReadNestedString(JsonElement element, params string[] path) =>
        TryReadPath(element, out var value, path) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryReadPath(JsonElement element, out JsonElement value, params string[] path)
    {
        value = element;
        foreach (var component in path)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(component, out value))
            {
                return false;
            }
        }

        return true;
    }

    private static string? FindStringProperty(JsonElement element, string property, int depth)
    {
        if (depth > 8)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            foreach (var child in element.EnumerateObject())
            {
                var found = FindStringProperty(child.Value, property, depth + 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in element.EnumerateArray())
            {
                var found = FindStringProperty(child, property, depth + 1);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    private static string? ExtractConfigurationValue(string html, string property)
    {
        var searchIndex = 0;
        while ((searchIndex = html.IndexOf($"\"{property}\"", searchIndex, StringComparison.Ordinal)) >= 0)
        {
            var colon = html.IndexOf(':', searchIndex + property.Length + 2);
            if (colon < 0)
            {
                return null;
            }

            var quoteStart = colon + 1;
            while (quoteStart < html.Length && char.IsWhiteSpace(html[quoteStart]))
            {
                quoteStart++;
            }

            if (quoteStart < html.Length && html[quoteStart] == '"' &&
                TryReadJsonString(html, quoteStart, out var token, out searchIndex))
            {
                try
                {
                    return JsonSerializer.Deserialize<string>(token);
                }
                catch (JsonException)
                {
                }
            }
            else
            {
                searchIndex += property.Length + 2;
            }
        }

        return null;
    }

    private static bool TryFindNextJsonObject(
        string source,
        string marker,
        int startIndex,
        out string json,
        out int nextIndex)
    {
        json = string.Empty;
        nextIndex = source.Length;
        var markerIndex = source.IndexOf(marker, startIndex, StringComparison.Ordinal);
        if (markerIndex < 0)
        {
            return false;
        }

        var objectStart = source.IndexOf('{', markerIndex + marker.Length);
        if (objectStart < 0 || objectStart - markerIndex > 256)
        {
            nextIndex = markerIndex + marker.Length;
            return TryFindNextJsonObject(source, marker, nextIndex, out json, out nextIndex);
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = objectStart; index < source.Length; index++)
        {
            var character = source[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && --depth == 0)
            {
                json = source[objectStart..(index + 1)];
                nextIndex = index + 1;
                return true;
            }
        }

        nextIndex = markerIndex + marker.Length;
        return false;
    }

    private static bool TryReadJsonString(
        string source,
        int quoteStart,
        out string token,
        out int nextIndex)
    {
        token = string.Empty;
        var escaped = false;
        for (var index = quoteStart + 1; index < source.Length; index++)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (source[index] == '\\')
            {
                escaped = true;
            }
            else if (source[index] == '"')
            {
                token = source[quoteStart..(index + 1)];
                nextIndex = index + 1;
                return true;
            }
        }

        nextIndex = source.Length;
        return false;
    }

    private static bool IsSafeConfigurationToken(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static bool IsSafeOpaqueToken(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= maximumLength &&
        value.All(character => !char.IsControl(character));

    private static Result<YouTubeCollectionPage> Failure(string detail, string? technicalDetail = null) =>
        Result<YouTubeCollectionPage>.Failure(new TubeForgeError(
            "Extractor.CollectionPageChanged",
            "TubeForge could not safely process the YouTube collection page.",
            technicalDetail ?? detail));

    private sealed class ParseState(bool includeTitle)
    {
        public bool IncludeTitle { get; } = includeTitle;

        public string Title { get; set; } = string.Empty;

        public List<YouTubeCollectionItem> Items { get; } = [];

        public string? ContinuationToken { get; set; }

        public int VisitedNodes { get; set; }
    }
}
