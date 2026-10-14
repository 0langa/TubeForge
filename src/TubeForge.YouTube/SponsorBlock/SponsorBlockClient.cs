using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Networking;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;

namespace TubeForge.YouTube.SponsorBlock;

public sealed class SponsorBlockClient
{
    private const int MaximumResponseCharacters = 2 * 1024 * 1024;
    private const int MaximumSegments = 1_000;
    private static readonly Uri DefaultOrigin = new("https://sponsor.ajay.app/");
    private readonly HttpClient _httpClient;
    private readonly Uri _origin;
    private readonly TimeSpan _stallTimeout;

    public SponsorBlockClient(HttpClient httpClient) : this(httpClient, DefaultOrigin)
    {
    }

    /// <param name="stallTimeout">
    /// The lookup runs while a queued download is being prepared, and the client's own timeout
    /// stops at the response headers, so a silent response body would otherwise hold the job.
    /// </param>
    internal SponsorBlockClient(HttpClient httpClient, Uri origin, TimeSpan? stallTimeout = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _stallTimeout = TransferStallGuard.Validate(stallTimeout);
        ArgumentNullException.ThrowIfNull(origin);
        if (!origin.IsAbsoluteUri || origin.Scheme is not ("http" or "https") ||
            string.IsNullOrWhiteSpace(origin.IdnHost) || origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.UserInfo) || !string.IsNullOrEmpty(origin.Query) ||
            !string.IsNullOrEmpty(origin.Fragment))
        {
            throw new ArgumentException("The SponsorBlock origin is invalid.", nameof(origin));
        }

        _origin = origin;
    }

    public async Task<Result<IReadOnlyList<SponsorBlockSegment>>> GetSegmentsAsync(
        YouTubeVideoId videoId,
        SponsorBlockSelection selection,
        CancellationToken cancellationToken = default)
    {
        if (!selection.IsValid)
        {
            return Failure("SponsorBlock.InvalidSelection", "Choose at least one valid SponsorBlock category.");
        }

        var prefix = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(videoId.Value)))[..4];
        var categories = JsonSerializer.Serialize(selection.ApiCategories);
        var relative = $"api/skipSegments/{prefix}?categories={Uri.EscapeDataString(categories)}" +
            $"&actionTypes={Uri.EscapeDataString("[\"skip\"]")}&service=YouTube&trimUUIDs=true";
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_origin, relative));
        _ = HttpUserAgentHeader.TryApply(request, "TubeForge/2.0 SponsorBlockClient");

        using var stall = new TransferStallGuard(_stallTimeout, cancellationToken);
        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                stall.Token).ConfigureAwait(false);
            stall.Progressed();
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return Result<IReadOnlyList<SponsorBlockSegment>>.Success([]);
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Result<IReadOnlyList<SponsorBlockSegment>>.Failure(new TubeForgeError(
                    "Network.RateLimited",
                    "SponsorBlock rate-limited this request. Try again later.",
                    IsTransient: true,
                    RetryAfter: HttpRetryAfterParser.Parse(response.Headers)));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    "SponsorBlock.RequestFailed",
                    "SponsorBlock did not return segment data.",
                    isTransient: (int)response.StatusCode >= 500);
            }

            if (response.Content.Headers.ContentLength is > MaximumResponseCharacters)
            {
                return Failure("SponsorBlock.ResponseTooLarge", "SponsorBlock returned too much segment data.");
            }

            var json = await ReadBoundedAsync(response.Content, stall).ConfigureAwait(false);
            return Parse(json, videoId, selection);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Failure("Operation.Cancelled", "SponsorBlock lookup was cancelled.");
        }
        catch (Exception exception) when ((exception is OperationCanceledException or IOException) && stall.HasStalled)
        {
            return Result<IReadOnlyList<SponsorBlockSegment>>.Failure(TransferStallGuard.StalledError(_stallTimeout));
        }
        catch (OperationCanceledException exception)
        {
            // HttpClient.Timeout expired before the headers arrived. Without this the timeout
            // escaped as a cancellation and the queued download was reported as paused.
            return Failure(
                "SponsorBlock.RequestFailed",
                "SponsorBlock did not respond in time.",
                exception.GetType().Name,
                isTransient: true);
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                "SponsorBlock.RequestFailed",
                "SponsorBlock could not be reached.",
                exception.GetType().Name,
                isTransient: true);
        }
        catch (JsonException exception)
        {
            return Failure(
                "SponsorBlock.InvalidResponse",
                "SponsorBlock returned invalid segment data.",
                exception.GetType().Name);
        }
    }

    private static Result<IReadOnlyList<SponsorBlockSegment>> Parse(
        string json,
        YouTubeVideoId videoId,
        SponsorBlockSelection selection)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return Failure("SponsorBlock.InvalidResponse", "SponsorBlock returned invalid segment data.");
        }

        foreach (var candidate in document.RootElement.EnumerateArray())
        {
            if (!candidate.TryGetProperty("videoID", out var id) || id.GetString() != videoId.Value ||
                !candidate.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            if (segments.GetArrayLength() > MaximumSegments)
            {
                return Failure("SponsorBlock.ResponseTooLarge", "SponsorBlock returned too much segment data.");
            }

            var parsed = new List<SponsorBlockSegment>();
            foreach (var item in segments.EnumerateArray())
            {
                if (!TryParseSegment(item, selection, out var segment))
                {
                    return Failure("SponsorBlock.InvalidResponse", "SponsorBlock returned invalid segment data.");
                }

                parsed.Add(segment);
            }

            return Result<IReadOnlyList<SponsorBlockSegment>>.Success(parsed
                .OrderBy(segment => segment.Start)
                .ThenBy(segment => segment.End)
                .ToArray());
        }

        return Result<IReadOnlyList<SponsorBlockSegment>>.Success([]);
    }

    private static bool TryParseSegment(
        JsonElement item,
        SponsorBlockSelection selection,
        out SponsorBlockSegment segment)
    {
        segment = null!;
        if (!item.TryGetProperty("segment", out var range) || range.ValueKind != JsonValueKind.Array ||
            range.GetArrayLength() != 2 ||
            !range[0].TryGetDouble(out var startSeconds) || !double.IsFinite(startSeconds) ||
            !range[1].TryGetDouble(out var endSeconds) || !double.IsFinite(endSeconds) ||
            !item.TryGetProperty("category", out var categoryElement) ||
            categoryElement.GetString() is not { } category ||
            !selection.ApiCategories.Contains(category, StringComparer.Ordinal) ||
            !item.TryGetProperty("actionType", out var actionType) || actionType.GetString() != "skip")
        {
            return false;
        }

        var description = item.TryGetProperty("description", out var descriptionElement)
            ? descriptionElement.GetString() ?? string.Empty
            : string.Empty;
        if (startSeconds < 0 || endSeconds <= startSeconds || endSeconds > MediaTrimRange.MaximumEnd.TotalSeconds)
        {
            return false;
        }

        segment = new SponsorBlockSegment(
            TimeSpan.FromMilliseconds(Math.Floor(startSeconds * 1_000)),
            TimeSpan.FromMilliseconds(Math.Ceiling(endSeconds * 1_000)),
            category,
            description);
        return segment.IsValid;
    }

    private static async Task<string> ReadBoundedAsync(
        HttpContent content,
        TransferStallGuard stall)
    {
        await using var stream = await content.ReadAsStreamAsync(stall.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[8_192];
        var builder = new StringBuilder();
        while (builder.Length <= MaximumResponseCharacters)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), stall.Token).ConfigureAwait(false);
            if (count == 0)
            {
                return builder.ToString();
            }

            stall.Progressed();
            builder.Append(buffer, 0, count);
        }

        throw new JsonException("SponsorBlock response exceeded its safe limit.");
    }

    private static Result<IReadOnlyList<SponsorBlockSegment>> Failure(
        string code,
        string message,
        string? detail = null,
        bool isTransient = false) =>
        Result<IReadOnlyList<SponsorBlockSegment>>.Failure(new TubeForgeError(
            code,
            message,
            detail,
            isTransient));
}
