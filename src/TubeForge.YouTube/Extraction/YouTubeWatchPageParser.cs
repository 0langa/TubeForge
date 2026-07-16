using System.Globalization;
using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;

namespace TubeForge.YouTube.Extraction;

public static class YouTubeWatchPageParser
{
    private const string PlayerResponseMarker = "ytInitialPlayerResponse";
    private static readonly Uri YouTubeOrigin = new("https://www.youtube.com/");

    public static Result<WatchPageData> Parse(
        string? html,
        Func<string, Uri?>? signatureCipherResolver = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return Failure("The watch page was empty.");
        }

        var searchIndex = 0;
        while (TryFindNextJsonObject(html, PlayerResponseMarker, searchIndex, out var json, out var nextIndex))
        {
            searchIndex = nextIndex;
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 64
                });

                var mapped = MapPlayerResponse(document.RootElement, html, signatureCipherResolver);
                if (mapped is not null)
                {
                    return mapped.Value;
                }
            }
            catch (JsonException)
            {
                // Another occurrence may contain the actual assigned player response.
            }
        }

        return Failure("The watch page did not contain a supported player response.");
    }

    internal static IReadOnlyList<string> ExtractSignatureCiphers(string html)
    {
        var ciphers = new List<string>();
        var searchIndex = 0;
        while (TryFindNextJsonObject(html, PlayerResponseMarker, searchIndex, out var json, out var nextIndex))
        {
            searchIndex = nextIndex;
            try
            {
                using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
                var root = document.RootElement;
                if (!root.TryGetProperty("streamingData", out var streamingData))
                {
                    continue;
                }

                CollectCiphers(streamingData, "formats", ciphers);
                CollectCiphers(streamingData, "adaptiveFormats", ciphers);
                if (ciphers.Count > 0)
                {
                    return ciphers;
                }
            }
            catch (JsonException)
            {
                // Try the next marker occurrence.
            }
        }

        return ciphers;
    }

    internal static string? ExtractConfigurationValue(string html, string property)
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
                    // Try another configuration occurrence.
                }
            }
            else
            {
                searchIndex += property.Length + 2;
            }
        }

        return null;
    }

    private static Result<WatchPageData>? MapPlayerResponse(
        JsonElement root,
        string html,
        Func<string, Uri?>? signatureCipherResolver)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("videoDetails", out var details) ||
            details.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var status = ReadString(root, "playabilityStatus", "status") ?? "UNKNOWN";
        if (!status.Equals("OK", StringComparison.OrdinalIgnoreCase))
        {
            var reason = ReadString(root, "playabilityStatus", "reason") ??
                         "YouTube reported that this video is unavailable.";
            var code = status.Equals("LOGIN_REQUIRED", StringComparison.OrdinalIgnoreCase)
                ? "Video.LoginRequired"
                : "Video.Unavailable";
            return Result<WatchPageData>.Failure(new TubeForgeError(code, reason));
        }

        var rawId = ReadString(details, "videoId");
        if (!YouTubeVideoId.TryCreate(rawId, out var videoId))
        {
            return Failure("The player response contained an invalid video ID.");
        }

        var title = ReadString(details, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return Failure("The player response did not contain a video title.");
        }

        var formats = new List<StreamFormat>();
        var cipheredCount = 0;
        if (root.TryGetProperty("streamingData", out var streamingData) &&
            streamingData.ValueKind == JsonValueKind.Object)
        {
            MapFormatArray(streamingData, "formats", formats, signatureCipherResolver, ref cipheredCount);
            MapFormatArray(streamingData, "adaptiveFormats", formats, signatureCipherResolver, ref cipheredCount);
        }

        var metadata = new VideoMetadata
        {
            Id = videoId,
            Title = title.Trim(),
            Channel = ReadString(details, "author")?.Trim() ?? string.Empty,
            Duration = ParseDuration(ReadString(details, "lengthSeconds")),
            ThumbnailUrl = ParseThumbnail(details),
            Availability = VideoAvailability.Available,
            Formats = formats
                .DistinctBy(format => format.FormatId)
                .ToArray()
        };

        return Result<WatchPageData>.Success(new WatchPageData(
            metadata,
            ParsePlayerScriptUrl(html),
            cipheredCount,
            status));
    }

    private static void MapFormatArray(
        JsonElement streamingData,
        string propertyName,
        ICollection<StreamFormat> output,
        Func<string, Uri?>? signatureCipherResolver,
        ref int cipheredCount)
    {
        if (!streamingData.TryGetProperty(propertyName, out var formats) ||
            formats.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var element in formats.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            Uri? resolvedUrl = null;
            if (element.TryGetProperty("url", out var urlProperty))
            {
                TryParseMediaUrl(urlProperty.GetString(), out resolvedUrl);
            }
            else if (TryReadCipher(element, out var cipher))
            {
                cipheredCount++;
                if (signatureCipherResolver is not null)
                {
                    resolvedUrl = signatureCipherResolver(cipher);
                }
            }

            if (resolvedUrl is null || !TryMapFormat(element, resolvedUrl, out var format))
            {
                continue;
            }

            output.Add(format);
        }
    }

    private static void CollectCiphers(
        JsonElement streamingData,
        string propertyName,
        ICollection<string> output)
    {
        if (!streamingData.TryGetProperty(propertyName, out var formats) ||
            formats.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var format in formats.EnumerateArray())
        {
            if (TryReadCipher(format, out var cipher))
            {
                output.Add(cipher);
            }
        }
    }

    private static bool TryReadCipher(JsonElement element, out string cipher)
    {
        cipher = string.Empty;
        foreach (var property in new[] { "signatureCipher", "cipher" })
        {
            if (element.TryGetProperty(property, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(value.GetString()))
            {
                cipher = value.GetString()!;
                return true;
            }
        }

        return false;
    }

    private static bool TryMapFormat(JsonElement element, Uri url, out StreamFormat format)
    {
        format = null!;
        if (!TryReadInt32(element, "itag", out var formatId) ||
            !element.TryGetProperty("mimeType", out var mimeProperty) ||
            mimeProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var mimeType = mimeProperty.GetString()!;
        var container = ParseContainer(mimeType);
        var codecs = ParseCodecs(mimeType);
        var videoCodec = ParseVideoCodec(codecs);
        var audioCodec = ParseAudioCodec(codecs);
        var mimeIsVideo = mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        var mimeIsAudio = mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        var hasVideo = mimeIsVideo || videoCodec is not VideoCodec.None and not VideoCodec.Unknown;
        var hasAudio = mimeIsAudio || audioCodec is not AudioCodec.None and not AudioCodec.Unknown;
        var kind = (hasVideo, hasAudio) switch
        {
            (true, true) => StreamKind.Progressive,
            (true, false) => StreamKind.VideoOnly,
            (false, true) => StreamKind.AudioOnly,
            _ => mimeIsVideo ? StreamKind.VideoOnly : StreamKind.AudioOnly
        };

        format = new StreamFormat
        {
            FormatId = formatId,
            Url = url,
            Container = container,
            Kind = kind,
            VideoCodec = hasVideo ? videoCodec : VideoCodec.None,
            AudioCodec = hasAudio ? audioCodec : AudioCodec.None,
            Width = ReadInt32(element, "width"),
            Height = ReadInt32(element, "height"),
            FramesPerSecond = ReadInt32(element, "fps"),
            Bitrate = ReadInt64(element, "bitrate"),
            ContentLength = ReadStringInt64(element, "contentLength"),
            AudioSampleRate = ReadStringInt32(element, "audioSampleRate"),
            IsHdr = ReadString(element, "qualityLabel")?.Contains("HDR", StringComparison.OrdinalIgnoreCase) == true,
            QualityLabel = ReadString(element, "qualityLabel") ?? ReadString(element, "audioQuality") ?? string.Empty
        };
        return true;
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

    private static Uri? ParsePlayerScriptUrl(string html)
    {
        foreach (var property in new[] { "jsUrl", "PLAYER_JS_URL" })
        {
            var searchIndex = 0;
            while ((searchIndex = html.IndexOf($"\"{property}\"", searchIndex, StringComparison.Ordinal)) >= 0)
            {
                var colon = html.IndexOf(':', searchIndex + property.Length + 2);
                if (colon < 0)
                {
                    break;
                }

                var quoteStart = colon + 1;
                while (quoteStart < html.Length && char.IsWhiteSpace(html[quoteStart]))
                {
                    quoteStart++;
                }

                if (quoteStart >= html.Length || html[quoteStart] != '"')
                {
                    searchIndex += property.Length + 2;
                    continue;
                }

                if (TryReadJsonString(html, quoteStart, out var rawToken, out searchIndex))
                {
                    try
                    {
                        var decoded = JsonSerializer.Deserialize<string>(rawToken);
                        if (!string.IsNullOrWhiteSpace(decoded) &&
                            Uri.TryCreate(YouTubeOrigin, decoded, out var uri) &&
                            uri.Scheme == Uri.UriSchemeHttps &&
                            uri.Host.EndsWith("youtube.com", StringComparison.OrdinalIgnoreCase))
                        {
                            return uri;
                        }
                    }
                    catch (JsonException)
                    {
                        // Try another configuration occurrence.
                    }
                }
            }
        }

        return null;
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

    private static bool TryParseMediaUrl(string? candidate, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
               parsed.Scheme == Uri.UriSchemeHttps &&
               (parsed.Host.Equals("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
                parsed.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)) &&
               (uri = parsed) is not null;
    }

    private static Uri? ParseThumbnail(JsonElement details)
    {
        if (!details.TryGetProperty("thumbnail", out var thumbnail) ||
            !thumbnail.TryGetProperty("thumbnails", out var thumbnails) ||
            thumbnails.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        Uri? selected = null;
        foreach (var candidate in thumbnails.EnumerateArray())
        {
            var url = ReadString(candidate, "url");
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            {
                selected = uri;
            }
        }

        return selected;
    }

    private static TimeSpan? ParseDuration(string? secondsText)
    {
        if (!long.TryParse(secondsText, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            seconds < 0 || seconds > TimeSpan.MaxValue.TotalSeconds)
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static MediaContainer ParseContainer(string mimeType) => mimeType.Split(';')[0].Trim() switch
    {
        "video/mp4" or "audio/mp4" => MediaContainer.Mp4,
        "video/webm" or "audio/webm" => MediaContainer.WebM,
        "video/3gpp" or "audio/3gpp" => MediaContainer.ThreeGp,
        _ => MediaContainer.Unknown
    };

    private static string[] ParseCodecs(string mimeType)
    {
        var marker = mimeType.IndexOf("codecs=\"", StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return [];
        }

        var start = marker + "codecs=\"".Length;
        var end = mimeType.IndexOf('"', start);
        return end < 0
            ? []
            : mimeType[start..end]
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static VideoCodec ParseVideoCodec(IEnumerable<string> codecs)
    {
        foreach (var codec in codecs)
        {
            if (codec.StartsWith("avc1", StringComparison.OrdinalIgnoreCase)) return VideoCodec.H264;
            if (codec.StartsWith("vp9", StringComparison.OrdinalIgnoreCase) ||
                codec.StartsWith("vp09", StringComparison.OrdinalIgnoreCase)) return VideoCodec.Vp9;
            if (codec.StartsWith("av01", StringComparison.OrdinalIgnoreCase)) return VideoCodec.Av1;
        }

        return VideoCodec.None;
    }

    private static AudioCodec ParseAudioCodec(IEnumerable<string> codecs)
    {
        foreach (var codec in codecs)
        {
            if (codec.StartsWith("mp4a", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Aac;
            if (codec.StartsWith("opus", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Opus;
            if (codec.StartsWith("vorbis", StringComparison.OrdinalIgnoreCase)) return AudioCodec.Vorbis;
        }

        return AudioCodec.None;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string? ReadString(JsonElement element, string parent, string property) =>
        element.TryGetProperty(parent, out var parentElement)
            ? ReadString(parentElement, property)
            : null;

    private static int? ReadInt32(JsonElement element, string property) =>
        TryReadInt32(element, property, out var value) ? value : null;

    private static bool TryReadInt32(JsonElement element, string property, out int value)
    {
        value = 0;
        return element.TryGetProperty(property, out var jsonValue) &&
               jsonValue.ValueKind == JsonValueKind.Number &&
               jsonValue.TryGetInt32(out value);
    }

    private static long? ReadInt64(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var parsed)
            ? parsed
            : null;

    private static long? ReadStringInt64(JsonElement element, string property) =>
        long.TryParse(ReadString(element, property), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static int? ReadStringInt32(JsonElement element, string property) =>
        int.TryParse(ReadString(element, property), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;

    private static Result<WatchPageData> Failure(string detail) =>
        Result<WatchPageData>.Failure(new TubeForgeError(
            "Extractor.PageChanged",
            "TubeForge could not read this YouTube watch page.",
            detail));
}
