using System.Buffers;
using System.Net;
using System.Text;
using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;

namespace TubeForge.YouTube.Collections;

public sealed class YouTubeCollectionResolver(HttpClient httpClient)
{
    private const int BufferSize = 32 * 1024;
    private const int MaximumCharacters = 10 * 1024 * 1024;
    private const int MaximumItems = 10_000;
    private const int MaximumPages = 100;
    private static readonly Uri ContinuationEndpoint = new("https://www.youtube.com/youtubei/v1/browse");
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<Result<YouTubeCollectionResult>> ResolveAsync(
        YouTubeCollectionReference source,
        int maximumItems = 1_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (maximumItems is < 1 or > MaximumItems || !IsAllowedYouTubeUrl(source.CanonicalUrl))
        {
            return Failure("Collection.InvalidRequest", "The collection request is invalid.");
        }

        try
        {
            using var initialRequest = new HttpRequestMessage(HttpMethod.Get, source.CanonicalUrl);
            AddBrowserHeaders(initialRequest);
            using var initialResponse = await _httpClient.SendAsync(
                initialRequest,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var finalUri = initialResponse.RequestMessage?.RequestUri ?? source.CanonicalUrl;
            if (!IsAllowedYouTubeUrl(finalUri))
            {
                return Failure("Collection.UnsafeRedirect", "The collection request redirected to an untrusted host.");
            }

            if (!initialResponse.IsSuccessStatusCode)
            {
                return HttpFailure(initialResponse.StatusCode);
            }

            var initialHtml = await ReadBoundedTextAsync(initialResponse.Content, cancellationToken)
                .ConfigureAwait(false);
            var initialPage = YouTubeCollectionPageParser.ParseInitialHtml(initialHtml);
            if (!initialPage.IsSuccess)
            {
                return Result<YouTubeCollectionResult>.Failure(initialPage.Error!);
            }

            var items = new List<YouTubeCollectionItem>();
            var seen = new HashSet<YouTubeVideoId>();
            AddItems(initialPage.Value.Items, items, seen, maximumItems);
            var title = initialPage.Value.Title;
            var token = initialPage.Value.ContinuationToken;
            var context = initialPage.Value.ContinuationContext;
            var pagesRead = 1;

            while (items.Count < maximumItems &&
                   pagesRead < MaximumPages &&
                   token is not null &&
                   context is not null)
            {
                using var request = BuildContinuationRequest(token, context);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);
                var continuationUri = response.RequestMessage?.RequestUri ?? ContinuationEndpoint;
                if (!IsAllowedYouTubeUrl(continuationUri))
                {
                    return Failure(
                        "Collection.UnsafeRedirect",
                        "The collection continuation redirected to an untrusted host.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return HttpFailure(response.StatusCode);
                }

                var json = await ReadBoundedTextAsync(response.Content, cancellationToken).ConfigureAwait(false);
                var page = YouTubeCollectionPageParser.ParseContinuationJson(json);
                if (!page.IsSuccess)
                {
                    return Result<YouTubeCollectionResult>.Failure(page.Error!);
                }

                AddItems(page.Value.Items, items, seen, maximumItems);
                token = page.Value.ContinuationToken;
                pagesRead++;
            }

            var truncated = token is not null || items.Count >= maximumItems;
            return Result<YouTubeCollectionResult>.Success(new YouTubeCollectionResult(
                source,
                string.IsNullOrWhiteSpace(title) ? source.Identifier : title,
                items,
                pagesRead,
                truncated));
        }
        catch (OperationCanceledException)
        {
            return Failure("Operation.Cancelled", "Collection analysis was cancelled.");
        }
        catch (ContentTooLargeException)
        {
            return Failure("Collection.PageTooLarge", "The collection page exceeds the safe size limit.");
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                "Network.RequestFailed",
                "The collection connection failed.",
                exception.GetType().Name,
                isTransient: true);
        }
    }

    private static HttpRequestMessage BuildContinuationRequest(
        string continuationToken,
        YouTubeContinuationContext context)
    {
        var endpoint = new UriBuilder(ContinuationEndpoint)
        {
            Query = $"key={Uri.EscapeDataString(context.ApiKey)}&prettyPrint=false"
        }.Uri;
        var body = new
        {
            context = new
            {
                client = new
                {
                    clientName = "WEB",
                    clientVersion = context.ClientVersion,
                    visitorData = context.VisitorData
                }
            },
            continuation = continuationToken
        };
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body))
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        AddBrowserHeaders(request);
        request.Headers.TryAddWithoutValidation("X-YouTube-Client-Name", "1");
        request.Headers.TryAddWithoutValidation("X-YouTube-Client-Version", context.ClientVersion);
        if (context.VisitorData is not null)
        {
            request.Headers.TryAddWithoutValidation("X-Goog-Visitor-Id", context.VisitorData);
        }

        return request;
    }

    private static void AddItems(
        IReadOnlyList<YouTubeCollectionItem> pageItems,
        ICollection<YouTubeCollectionItem> output,
        ISet<YouTubeVideoId> seen,
        int maximumItems)
    {
        foreach (var item in pageItems)
        {
            if (output.Count >= maximumItems)
            {
                return;
            }

            if (!seen.Add(item.VideoId))
            {
                continue;
            }

            output.Add(item.Index is null or < 1
                ? item with { Index = output.Count + 1 }
                : item);
        }
    }

    private static async Task<string> ReadBoundedTextAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumCharacters * 4L)
        {
            throw new ContentTooLargeException();
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var decoder = Encoding.UTF8.GetDecoder();
        var byteBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var charBuffer = ArrayPool<char>.Shared.Rent(BufferSize);
        var builder = new StringBuilder(Math.Min(MaximumCharacters, 512 * 1024));
        try
        {
            while (true)
            {
                var count = await stream.ReadAsync(byteBuffer.AsMemory(0, BufferSize), cancellationToken)
                    .ConfigureAwait(false);
                var flush = count == 0;
                decoder.Convert(
                    byteBuffer,
                    0,
                    count,
                    charBuffer,
                    0,
                    charBuffer.Length,
                    flush,
                    out _,
                    out var characters,
                    out _);
                if (builder.Length > MaximumCharacters - characters)
                {
                    throw new ContentTooLargeException();
                }

                builder.Append(charBuffer, 0, characters);
                if (flush)
                {
                    return builder.ToString();
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(byteBuffer);
            ArrayPool<char>.Shared.Return(charBuffer);
        }
    }

    private static void AddBrowserHeaders(HttpRequestMessage request)
    {
        request.Headers.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
        request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
        request.Headers.TryAddWithoutValidation("Cookie", "SOCS=CAI");
    }

    private static bool IsAllowedYouTubeUrl(Uri uri) =>
        uri.IsAbsoluteUri &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase));

    private static Result<YouTubeCollectionResult> HttpFailure(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.TooManyRequests => Failure(
            "Network.RateLimited",
            "YouTube temporarily rate-limited collection analysis.",
            isTransient: true),
        HttpStatusCode.Forbidden => Failure(
            "Network.Forbidden",
            "YouTube refused the collection analysis request."),
        _ => Failure(
            "Network.HttpError",
            $"YouTube returned HTTP {(int)statusCode} during collection analysis.",
            isTransient: (int)statusCode >= 500)
    };

    private static Result<YouTubeCollectionResult> Failure(
        string code,
        string message,
        string? detail = null,
        bool isTransient = false) =>
        Result<YouTubeCollectionResult>.Failure(new TubeForgeError(code, message, detail, isTransient));

    private sealed class ContentTooLargeException : Exception;
}
