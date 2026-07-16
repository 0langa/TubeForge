using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Collections.Concurrent;
using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;
using TubeForge.YouTube.Extraction;
using TubeForge.YouTube.Player;

namespace TubeForge.YouTube;

public sealed class YouTubeMetadataResolver(HttpClient httpClient)
{
    private const int MaximumWatchPageCharacters = 8 * 1024 * 1024;
    private const int MaximumPlayerScriptCharacters = 6 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly ConcurrentDictionary<string, SignatureTransformPlan> TransformCache = new();

    public async Task<Result<WatchPageData>> ResolveAsync(
        YouTubeVideoId videoId,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(RequestTimeout);

        try
        {
            var url = new Uri($"https://www.youtube.com/watch?v={Uri.EscapeDataString(videoId.Value)}&hl=en");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            AddBrowserHeaders(request);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return HttpFailure(response.StatusCode);
            }

            if (response.Content.Headers.ContentLength is > MaximumWatchPageCharacters)
            {
                return ExtractorFailure("The watch page exceeded the safe size limit.");
            }

            var html = await ReadBoundedTextAsync(
                response.Content,
                MaximumWatchPageCharacters,
                timeoutSource.Token).ConfigureAwait(false);
            var watchResult = YouTubeWatchPageParser.Parse(html);
            if (!watchResult.IsSuccess ||
                watchResult.Value.Metadata.Formats.Count > 0 ||
                watchResult.Value.CipheredFormatCount == 0 ||
                watchResult.Value.PlayerScriptUrl is null)
            {
                return watchResult;
            }

            var clientResult = await TryResolveWithAndroidClientAsync(
                html,
                watchResult.Value,
                timeoutSource.Token).ConfigureAwait(false);
            if (clientResult is not null && clientResult.Metadata.Formats.Count > 0)
            {
                return Result<WatchPageData>.Success(clientResult);
            }

            return await TryResolveCipheredFormatsAsync(
                html,
                watchResult.Value,
                url,
                timeoutSource.Token).ConfigureAwait(false);
        }
        catch (ContentTooLargeException)
        {
            return ExtractorFailure("The watch page exceeded the safe size limit.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<WatchPageData>.Failure(new TubeForgeError(
                "Operation.Cancelled",
                "Video analysis was cancelled."));
        }
        catch (OperationCanceledException)
        {
            return Result<WatchPageData>.Failure(new TubeForgeError(
                "Network.Timeout",
                "YouTube did not respond before the analysis timeout.",
                IsTransient: true));
        }
        catch (HttpRequestException exception)
        {
            return Result<WatchPageData>.Failure(new TubeForgeError(
                "Network.RequestFailed",
                "TubeForge could not connect to YouTube.",
                exception.GetType().Name,
                IsTransient: true));
        }
        catch (IOException exception)
        {
            return Result<WatchPageData>.Failure(new TubeForgeError(
                "Network.ReadFailed",
                "TubeForge could not read YouTube's response.",
                exception.GetType().Name,
                IsTransient: true));
        }
    }

    private async Task<WatchPageData?> TryResolveWithAndroidClientAsync(
        string html,
        WatchPageData fallback,
        CancellationToken cancellationToken)
    {
        var apiKey = YouTubeWatchPageParser.ExtractConfigurationValue(html, "INNERTUBE_API_KEY");
        if (!IsSafeConfigurationToken(apiKey))
        {
            return null;
        }

        var visitorData = YouTubeWatchPageParser.ExtractConfigurationValue(html, "VISITOR_DATA");
        var profile = YouTubeClientProfile.Android;
        var payload = new
        {
            context = new
            {
                client = new
                {
                    clientName = profile.Name,
                    clientVersion = profile.Version,
                    hl = "en",
                    gl = "US",
                    visitorData
                }
            },
            videoId = fallback.Metadata.Id.Value,
            contentCheckOk = true,
            racyCheckOk = true
        };

        try
        {
            var endpoint = new Uri(
                $"https://www.youtube.com/youtubei/v1/player?prettyPrint=false&key={Uri.EscapeDataString(apiKey!)}");
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.UserAgent.ParseAdd(profile.UserAgent);
            request.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", profile.NumericId);
            request.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", profile.Version);
            if (!string.IsNullOrWhiteSpace(visitorData))
            {
                request.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", visitorData);
            }

            request.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");
            using var response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode ||
                response.Content.Headers.ContentLength is > MaximumWatchPageCharacters)
            {
                return null;
            }

            var json = await ReadBoundedTextAsync(
                response.Content,
                MaximumWatchPageCharacters,
                cancellationToken).ConfigureAwait(false);
            var parsed = YouTubeWatchPageParser.Parse("ytInitialPlayerResponse=" + json + ";");
            if (!parsed.IsSuccess || parsed.Value.Metadata.Id != fallback.Metadata.Id)
            {
                return null;
            }

            return parsed.Value with
            {
                Metadata = parsed.Value.Metadata with
                {
                    CaptionTracks = parsed.Value.Metadata.CaptionTracks.Count > 0
                        ? parsed.Value.Metadata.CaptionTracks
                        : fallback.Metadata.CaptionTracks
                },
                PlayerScriptUrl = fallback.PlayerScriptUrl,
                Diagnostics = new ExtractionDiagnostics("AndroidClientResolved")
            };
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (ContentTooLargeException)
        {
            return null;
        }
    }

    private async Task<Result<WatchPageData>> TryResolveCipheredFormatsAsync(
        string html,
        WatchPageData fallback,
        Uri watchUrl,
        CancellationToken cancellationToken)
    {
        var playerScriptUrl = fallback.PlayerScriptUrl!;
        if (TransformCache.TryGetValue(playerScriptUrl.AbsolutePath, out var cachedPlan))
        {
            var cachedResult = ParseWithPlan(html, cachedPlan);
            if (cachedResult.IsSuccess && cachedResult.Value.Metadata.Formats.Count > 0)
            {
                return cachedResult;
            }

            TransformCache.TryRemove(playerScriptUrl.AbsolutePath, out _);
        }

        try
        {
            using var scriptRequest = new HttpRequestMessage(HttpMethod.Get, playerScriptUrl);
            AddBrowserHeaders(scriptRequest);
            scriptRequest.Headers.Accept.Clear();
            scriptRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/javascript"));
            using var scriptResponse = await httpClient.SendAsync(
                scriptRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!scriptResponse.IsSuccessStatusCode ||
                scriptResponse.Content.Headers.ContentLength is > MaximumPlayerScriptCharacters)
            {
                return WithDiagnostics(fallback, "PlayerScriptUnavailable");
            }

            var script = await ReadBoundedTextAsync(
                scriptResponse.Content,
                MaximumPlayerScriptCharacters,
                cancellationToken).ConfigureAwait(false);
            var plans = SignatureTransformExtractor.Extract(script);
            var cipher = YouTubeWatchPageParser.ExtractSignatureCiphers(html).FirstOrDefault();
            if (cipher is null)
            {
                return WithDiagnostics(fallback, "CipherMissing", plans.Count);
            }

            var probeAttempts = 0;
            foreach (var plan in plans.Take(8))
            {
                var candidate = SignatureCipherUrl.Resolve(cipher, plan);
                if (candidate is null)
                {
                    continue;
                }

                probeAttempts++;
                if (!await ProbeMediaUrlAsync(candidate, watchUrl, cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                var resolved = ParseWithPlan(html, plan);
                if (resolved.IsSuccess && resolved.Value.Metadata.Formats.Count > 0)
                {
                    TransformCache[playerScriptUrl.AbsolutePath] = plan;
                    return resolved;
                }
            }

            return WithDiagnostics(
                fallback,
                plans.Count == 0 ? "TransformPlanMissing" : "TransformPlanRejected",
                plans.Count,
                probeAttempts);
        }
        catch (ContentTooLargeException)
        {
            return WithDiagnostics(fallback, "PlayerScriptTooLarge");
        }
        catch (HttpRequestException)
        {
            return WithDiagnostics(fallback, "PlayerScriptRequestFailed");
        }
        catch (IOException)
        {
            return WithDiagnostics(fallback, "PlayerScriptReadFailed");
        }
    }

    private async Task<bool> ProbeMediaUrlAsync(
        Uri mediaUrl,
        Uri watchUrl,
        CancellationToken cancellationToken)
    {
        using var probe = new HttpRequestMessage(HttpMethod.Get, mediaUrl);
        AddBrowserHeaders(probe);
        probe.Headers.Accept.Clear();
        probe.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        probe.Headers.Range = new RangeHeaderValue(0, 0);
        probe.Headers.Referrer = watchUrl;
        using var response = await httpClient.SendAsync(
            probe,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri ?? mediaUrl;
        return response.IsSuccessStatusCode &&
               finalUri.Scheme == Uri.UriSchemeHttps &&
               (finalUri.Host.Equals("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
                finalUri.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase));
    }

    private static Result<WatchPageData> ParseWithPlan(string html, SignatureTransformPlan plan) =>
        YouTubeWatchPageParser.Parse(html, cipher => SignatureCipherUrl.Resolve(cipher, plan));

    private static Result<WatchPageData> WithDiagnostics(
        WatchPageData fallback,
        string stage,
        int planCount = 0,
        int probeCount = 0) =>
        Result<WatchPageData>.Success(fallback with
        {
            Diagnostics = new ExtractionDiagnostics(stage, planCount, probeCount)
        });

    private static void AddBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
    }

    private static bool IsSafeConfigurationToken(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);
        var buffer = new char[16 * 1024];
        var builder = new StringBuilder(Math.Min(maximumCharacters, 512 * 1024));

        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                return builder.ToString();
            }

            if (builder.Length > maximumCharacters - count)
            {
                throw new ContentTooLargeException();
            }

            builder.Append(buffer, 0, count);
        }
    }

    private static Result<WatchPageData> HttpFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => Result<WatchPageData>.Failure(new TubeForgeError(
            "Network.RateLimited", "YouTube temporarily rate-limited this device.", IsTransient: true)),
        HttpStatusCode.Forbidden => Result<WatchPageData>.Failure(new TubeForgeError(
            "Network.Forbidden", "YouTube refused the video analysis request.")),
        _ => Result<WatchPageData>.Failure(new TubeForgeError(
            "Network.HttpError",
            $"YouTube returned HTTP {(int)statusCode} while analyzing the video.",
            IsTransient: (int)statusCode >= 500))
    };

    private static Result<WatchPageData> ExtractorFailure(string detail) =>
        Result<WatchPageData>.Failure(new TubeForgeError(
            "Extractor.PageChanged",
            "TubeForge could not safely process the YouTube watch page.",
            detail));

    private sealed class ContentTooLargeException : Exception;
}
