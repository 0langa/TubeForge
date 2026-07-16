using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;
using TubeForge.Downloads.Resume;
using TubeForge.Media;

namespace TubeForge.Downloads;

public sealed class DirectDownloadEngine
{
    private const int BufferSize = 128 * 1024;
    private readonly HttpClient _httpClient;
    private readonly DownloadUriPolicy _uriPolicy;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<string, bool, Stream> _outputStreamFactory;
    private readonly SegmentedDownloadEngine _segmentedDownloadEngine;

    public DirectDownloadEngine(
        HttpClient httpClient,
        DownloadUriPolicy? uriPolicy = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
        : this(httpClient, uriPolicy, delay, outputStreamFactory: null)
    {
    }

    internal DirectDownloadEngine(
        HttpClient httpClient,
        DownloadUriPolicy? uriPolicy,
        Func<TimeSpan, CancellationToken, Task>? delay,
        Func<string, bool, Stream>? outputStreamFactory)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _uriPolicy = uriPolicy ?? DownloadUriPolicy.YouTubeMediaOnly;
        _delay = delay ?? Task.Delay;
        _outputStreamFactory = outputStreamFactory ?? CreateOutputStream;
        _segmentedDownloadEngine = new SegmentedDownloadEngine(_httpClient, _uriPolicy);
    }

    public async Task<Result<DownloadReceipt>> DownloadAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validationFailure = ValidateRequest(request);
        if (validationFailure is not null)
        {
            return Result<DownloadReceipt>.Failure(validationFailure);
        }

        var useSegmentedTransfer = SegmentedDownloadEngine.ShouldUse(request);
        for (var attempt = 1; attempt <= DownloadRetryPolicy.MaximumAttempts; attempt++)
        {
            var result = useSegmentedTransfer
                ? await _segmentedDownloadEngine.DownloadAttemptAsync(request, progress, cancellationToken)
                    .ConfigureAwait(false)
                : await DownloadAttemptAsync(request, progress, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess && result.Error?.Code == "Download.RangeUnsupported")
            {
                SegmentedDownloadEngine.Reset(request.DestinationPath);
                useSegmentedTransfer = false;
                result = await DownloadAttemptAsync(request, progress, cancellationToken).ConfigureAwait(false);
            }

            if (result.IsSuccess ||
                result.Error?.IsTransient != true ||
                attempt == DownloadRetryPolicy.MaximumAttempts ||
                cancellationToken.IsCancellationRequested)
            {
                return result;
            }

            try
            {
                await _delay(DownloadRetryPolicy.DelayBeforeAttempt(attempt), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return Cancelled();
            }
        }

        throw new UnreachableException();
    }

    private async Task<Result<DownloadReceipt>> DownloadAttemptAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var destinationPath = Path.GetFullPath(request.DestinationPath);
        var partialPath = destinationPath + ".part";
        var statePath = destinationPath + ".part.json";
        try
        {
            if (File.Exists(destinationPath))
            {
                return Failure(
                    "Download.DestinationExists",
                    "A file already exists at the selected destination.");
            }

            var directory = Path.GetDirectoryName(destinationPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Failure("Download.InvalidDestination", "The selected destination is invalid.");
            }

            Directory.CreateDirectory(directory);
            var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
            var state = existingLength > 0
                ? await DownloadResumeStore.ReadAsync(statePath, cancellationToken).ConfigureAwait(false)
                : null;
            var canResume = existingLength > 0 && IsCompatible(state, request, existingLength);
            if (!canResume)
            {
                existingLength = 0;
            }

            using var requestMessage = new HttpRequestMessage(HttpMethod.Get, request.SourceUrl);
            requestMessage.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
            if (canResume)
            {
                requestMessage.Headers.Range = new RangeHeaderValue(existingLength, null);
                if (!string.IsNullOrWhiteSpace(state!.EntityTag) &&
                    EntityTagHeaderValue.TryParse(state.EntityTag, out var entityTag))
                {
                    requestMessage.Headers.IfRange = new RangeConditionHeaderValue(entityTag);
                }
                else if (state.LastModified is not null)
                {
                    requestMessage.Headers.IfRange = new RangeConditionHeaderValue(state.LastModified.Value);
                }
            }

            using var response = await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var finalUri = response.RequestMessage?.RequestUri ?? request.SourceUrl;
            if (!_uriPolicy.IsAllowed(finalUri))
            {
                return Failure(
                    "Download.UnsafeRedirect",
                    "The media request redirected to an untrusted location.");
            }

            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable &&
                canResume && request.ExpectedLength == existingLength)
            {
                return FinalizeCompletedPartial(
                    destinationPath,
                    partialPath,
                    statePath,
                    existingLength,
                    resumed: true,
                    request.ExpectedContainer);
            }

            if (!response.IsSuccessStatusCode)
            {
                return HttpFailure(response.StatusCode);
            }

            var append = canResume && response.StatusCode == HttpStatusCode.PartialContent;
            if (append && response.Content.Headers.ContentRange?.From != existingLength)
            {
                return Failure(
                    "Download.RemoteChanged",
                    "The server returned an incompatible resume range.");
            }

            if (!append)
            {
                existingLength = 0;
            }

            var totalLength = DetermineTotalLength(response, existingLength);
            if (request.ExpectedLength is not null &&
                totalLength is not null &&
                request.ExpectedLength != totalLength)
            {
                return Failure(
                    "Download.RemoteChanged",
                    "The remote media size changed before the download started.");
            }

            var newState = new DownloadResumeState
            {
                SourceIdentity = request.SourceIdentity,
                ExpectedLength = request.ExpectedLength ?? totalLength,
                EntityTag = response.Headers.ETag?.ToString(),
                LastModified = response.Content.Headers.LastModified
            };
            await DownloadResumeStore.WriteAsync(statePath, newState, cancellationToken).ConfigureAwait(false);

            var transferred = await CopyResponseAsync(
                response,
                partialPath,
                append,
                existingLength,
                request.ExpectedLength ?? totalLength,
                progress,
                cancellationToken).ConfigureAwait(false);
            var finalLength = existingLength + transferred;
            var expectedLength = request.ExpectedLength ?? totalLength;
            if (expectedLength is not null && finalLength != expectedLength)
            {
                return Result<DownloadReceipt>.Failure(new TubeForgeError(
                    "Download.Incomplete",
                    "The server ended the transfer before all media bytes arrived.",
                    $"Expected {expectedLength} bytes; received {finalLength}.",
                    IsTransient: true));
            }

            return FinalizeCompletedPartial(
                destinationPath,
                partialPath,
                statePath,
                finalLength,
                append,
                request.ExpectedContainer);
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }
        catch (HttpRequestException exception)
        {
            return Result<DownloadReceipt>.Failure(new TubeForgeError(
                "Network.RequestFailed",
                "The media connection failed.",
                exception.GetType().Name,
                IsTransient: true));
        }
        catch (UnauthorizedAccessException exception)
        {
            return Result<DownloadReceipt>.Failure(new TubeForgeError(
                "Download.WriteFailed",
                "TubeForge does not have permission to write the selected file.",
                exception.GetType().Name));
        }
        catch (IOException exception) when (IsDiskFull(exception))
        {
            return Result<DownloadReceipt>.Failure(new TubeForgeError(
                "Download.DiskFull",
                "The destination drive is full.",
                exception.GetType().Name));
        }
        catch (IOException exception)
        {
            return Result<DownloadReceipt>.Failure(new TubeForgeError(
                "Download.TransferFailed",
                "The media transfer was interrupted.",
                exception.GetType().Name,
                IsTransient: true));
        }
    }

    private async Task<long> CopyResponseAsync(
        HttpResponseMessage response,
        string partialPath,
        bool append,
        long startingLength,
        long? totalLength,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var output = _outputStreamFactory(partialPath, append);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var stopwatch = Stopwatch.StartNew();
        var transferred = 0L;
        var lastReport = TimeSpan.Zero;

        try
        {
            while (true)
            {
                var count = await input.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                transferred += count;

                if (progress is not null &&
                    (stopwatch.Elapsed - lastReport >= TimeSpan.FromMilliseconds(100) ||
                     startingLength + transferred == totalLength))
                {
                    lastReport = stopwatch.Elapsed;
                    ReportProgress(progress, startingLength + transferred, totalLength, transferred, stopwatch.Elapsed);
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            return transferred;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static Stream CreateOutputStream(string partialPath, bool append) => new FileStream(
        partialPath,
        append ? FileMode.Append : FileMode.Create,
        FileAccess.Write,
        FileShare.Read,
        BufferSize,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void ReportProgress(
        IProgress<DownloadProgress> progress,
        long bytesReceived,
        long? totalBytes,
        long attemptBytes,
        TimeSpan elapsed)
    {
        var speed = elapsed.TotalSeconds > 0 ? attemptBytes / elapsed.TotalSeconds : 0;
        TimeSpan? remaining = totalBytes is not null && speed > 0
            ? TimeSpan.FromSeconds(Math.Max(0, totalBytes.Value - bytesReceived) / speed)
            : null;
        progress.Report(new DownloadProgress(bytesReceived, totalBytes, speed, remaining));
    }

    internal static Result<DownloadReceipt> FinalizeCompletedPartial(
        string destinationPath,
        string partialPath,
        string statePath,
        long finalLength,
        bool resumed,
        MediaContainer expectedContainer)
    {
        if (!File.Exists(partialPath) || new FileInfo(partialPath).Length != finalLength)
        {
            return Failure(
                "Download.Incomplete",
                "The partial file did not pass final length validation.");
        }

        if (expectedContainer != MediaContainer.Unknown)
        {
            var validation = MediaContainerValidator.Validate(partialPath, expectedContainer);
            if (!validation.IsSuccess)
            {
                return Result<DownloadReceipt>.Failure(validation.Error!);
            }
        }

        File.Move(partialPath, destinationPath, overwrite: false);
        File.Delete(statePath);
        return Result<DownloadReceipt>.Success(new DownloadReceipt(destinationPath, finalLength, resumed));
    }

    private static bool IsCompatible(
        DownloadResumeState? state,
        DownloadRequest request,
        long partialLength) =>
        state is not null &&
        state.SourceIdentity.Equals(request.SourceIdentity, StringComparison.Ordinal) &&
        (request.ExpectedLength is null || state.ExpectedLength is null || request.ExpectedLength == state.ExpectedLength) &&
        (state.ExpectedLength is null || partialLength <= state.ExpectedLength);

    private static long? DetermineTotalLength(HttpResponseMessage response, long startingLength)
    {
        if (response.StatusCode == HttpStatusCode.PartialContent &&
            response.Content.Headers.ContentRange?.Length is long rangeLength)
        {
            return rangeLength;
        }

        return response.Content.Headers.ContentLength is long contentLength
            ? startingLength + contentLength
            : null;
    }

    private TubeForgeError? ValidateRequest(DownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_uriPolicy.IsAllowed(request.SourceUrl))
        {
            return new TubeForgeError(
                "Download.UnsafeSource",
                "The selected media URL is not an approved YouTube media endpoint.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceIdentity) || request.SourceIdentity.Length > 256)
        {
            return new TubeForgeError(
                "Download.InvalidSourceIdentity",
                "The media source identity is invalid.");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return new TubeForgeError("Download.InvalidDestination", "Select a destination file.");
        }

        if (request.ExpectedLength is <= 0)
        {
            return new TubeForgeError("Download.InvalidLength", "The expected media size is invalid.");
        }

        if (request.EnableSegmentedTransfer &&
            (request.MaximumSegments is < 2 or > 8 || request.SegmentedTransferMinimumBytes <= 0))
        {
            return new TubeForgeError(
                "Download.InvalidSegmentation",
                "The segmented transfer settings are invalid.");
        }

        return null;
    }

    internal static Result<DownloadReceipt> HttpFailure(HttpStatusCode statusCode)
    {
        var transient = statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
                        (int)statusCode >= 500;
        return Result<DownloadReceipt>.Failure(new TubeForgeError(
            statusCode == HttpStatusCode.TooManyRequests ? "Network.RateLimited" : "Network.HttpError",
            $"The media server returned HTTP {(int)statusCode}.",
            IsTransient: transient));
    }

    private static Result<DownloadReceipt> Failure(string code, string message) =>
        Result<DownloadReceipt>.Failure(new TubeForgeError(code, message));

    private static Result<DownloadReceipt> Cancelled() =>
        Result<DownloadReceipt>.Failure(new TubeForgeError(
            "Operation.Cancelled",
            "The download was cancelled."));

    private static bool IsDiskFull(IOException exception) =>
        (exception.HResult & 0xFFFF) is 0x27 or 0x70;
}
