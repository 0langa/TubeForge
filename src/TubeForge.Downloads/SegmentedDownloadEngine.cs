using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Downloads;

internal sealed class SegmentedDownloadEngine(HttpClient httpClient, DownloadUriPolicy uriPolicy)
{
    private const int BufferSize = 128 * 1024;
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly DownloadUriPolicy _uriPolicy = uriPolicy ?? throw new ArgumentNullException(nameof(uriPolicy));

    public static bool ShouldUse(DownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.EnableSegmentedTransfer &&
               request.ExpectedLength is long expectedLength &&
               expectedLength >= request.SegmentedTransferMinimumBytes &&
               !File.Exists(Path.GetFullPath(request.DestinationPath) + ".part.json");
    }

    public static void Reset(string destinationPath)
    {
        var destination = Path.GetFullPath(destinationPath);
        TryDelete(destination + ".part");
        TryDelete(StatePath(destination));
        TryDelete(StatePath(destination) + ".new");
    }

    public async Task<Result<DownloadReceipt>> DownloadAttemptAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var destination = Path.GetFullPath(request.DestinationPath);
        var partialPath = destination + ".part";
        var statePath = StatePath(destination);
        try
        {
            if (File.Exists(destination))
            {
                return Failure("Download.DestinationExists", "A file already exists at the selected destination.");
            }

            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Failure("Download.InvalidDestination", "The selected destination is invalid.");
            }

            Directory.CreateDirectory(directory);
            var expectedLength = request.ExpectedLength!.Value;
            var segments = CreateSegments(expectedLength, request.MaximumSegments);
            var state = await SegmentedDownloadStateStore.ReadAsync(statePath, cancellationToken)
                .ConfigureAwait(false);
            if (!IsCompatible(state, request, segments) ||
                !File.Exists(partialPath) ||
                new FileInfo(partialPath).Length != expectedLength)
            {
                Reset(destination);
                await using (var initializer = new FileStream(
                                 partialPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.Read,
                                 bufferSize: 1,
                                 FileOptions.WriteThrough | FileOptions.RandomAccess))
                {
                    initializer.SetLength(expectedLength);
                    initializer.Flush(flushToDisk: true);
                }

                state = new SegmentedDownloadState
                {
                    SourceIdentity = request.SourceIdentity,
                    ExpectedLength = expectedLength,
                    SegmentCount = segments.Length,
                    Completed = new bool[segments.Length]
                };
                await SegmentedDownloadStateStore.WriteAsync(statePath, state, cancellationToken)
                    .ConfigureAwait(false);
            }

            var activeState = state!;
            var resumed = activeState.Completed.Any(completed => completed);
            var completedBytes = segments
                .Where((_, index) => activeState.Completed[index])
                .Sum(segment => segment.Length);
            var attemptBytes = 0L;
            var stopwatch = Stopwatch.StartNew();
            using var stateGate = new SemaphoreSlim(1, 1);
            var currentState = activeState;
            await using (var output = new FileStream(
                             partialPath,
                             FileMode.Open,
                             FileAccess.Write,
                             FileShare.Read,
                             bufferSize: 1,
                             FileOptions.Asynchronous | FileOptions.RandomAccess))
            {
                progress?.Report(Progress(completedBytes, expectedLength, attemptBytes, stopwatch.Elapsed));
                var tasks = segments
                    .Where((_, index) => !activeState.Completed[index])
                    .Select(segment => DownloadSegmentAsync(
                        request,
                        segment,
                        output.SafeFileHandle,
                        stateGate,
                        () => currentState,
                        value => currentState = value,
                        statePath,
                        bytes =>
                        {
                            Interlocked.Add(ref completedBytes, bytes);
                            var currentAttemptBytes = Interlocked.Add(ref attemptBytes, bytes);
                            progress?.Report(Progress(
                                Interlocked.Read(ref completedBytes),
                                expectedLength,
                                currentAttemptBytes,
                                stopwatch.Elapsed));
                        },
                        cancellationToken))
                    .ToArray();
                var results = await Task.WhenAll(tasks).ConfigureAwait(false);
                var failure = results.FirstOrDefault(result => !result.IsSuccess);
                if (failure.Error is not null)
                {
                    return Result<DownloadReceipt>.Failure(failure.Error);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            if (!currentState.Completed.All(completed => completed))
            {
                return Failure(
                    "Download.Incomplete",
                    "One or more segmented ranges did not complete.",
                    isTransient: true);
            }

            return DirectDownloadEngine.FinalizeCompletedPartial(
                destination,
                partialPath,
                statePath,
                expectedLength,
                resumed,
                request.ExpectedContainer);
        }
        catch (OperationCanceledException)
        {
            return Failure("Operation.Cancelled", "The download was cancelled.");
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                "Network.RequestFailed",
                "The media connection failed.",
                exception.GetType().Name,
                isTransient: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Failure(
                "Download.WriteFailed",
                "TubeForge does not have permission to write the selected file.",
                exception.GetType().Name);
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) is 0x27 or 0x70)
        {
            return Failure("Download.DiskFull", "The destination drive is full.", exception.GetType().Name);
        }
        catch (IOException exception)
        {
            return Failure(
                "Download.TransferFailed",
                "The segmented media transfer was interrupted.",
                exception.GetType().Name,
                isTransient: true);
        }
        catch (JsonException)
        {
            Reset(destination);
            return Failure(
                "Download.SegmentStateInvalid",
                "The segmented transfer state was invalid and has been reset.",
                isTransient: true);
        }
    }

    private async Task<Result<bool>> DownloadSegmentAsync(
        DownloadRequest request,
        ByteRange segment,
        SafeFileHandle output,
        SemaphoreSlim stateGate,
        Func<SegmentedDownloadState> getState,
        Action<SegmentedDownloadState> setState,
        string statePath,
        Action<int> reportBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, request.SourceUrl);
            message.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
            message.Headers.Range = new RangeHeaderValue(segment.Start, segment.End);
            var initialState = getState();
            if (!string.IsNullOrWhiteSpace(initialState.EntityTag) &&
                EntityTagHeaderValue.TryParse(initialState.EntityTag, out var entityTag))
            {
                message.Headers.IfRange = new RangeConditionHeaderValue(entityTag);
            }

            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var finalUri = response.RequestMessage?.RequestUri ?? request.SourceUrl;
            if (!_uriPolicy.IsAllowed(finalUri))
            {
                return FailureBool("Download.UnsafeRedirect", "The media request redirected to an untrusted location.");
            }

            if (response.StatusCode == HttpStatusCode.OK)
            {
                return FailureBool(
                    "Download.RangeUnsupported",
                    "The media server does not support segmented byte ranges.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var httpFailure = DirectDownloadEngine.HttpFailure(response);
                return Result<bool>.Failure(httpFailure.Error!);
            }

            var contentRange = response.Content.Headers.ContentRange;
            if (response.StatusCode != HttpStatusCode.PartialContent ||
                contentRange?.From != segment.Start ||
                contentRange.To != segment.End ||
                contentRange.Length != request.ExpectedLength ||
                response.Content.Headers.ContentLength is long contentLength && contentLength != segment.Length)
            {
                return FailureBool(
                    "Download.RemoteChanged",
                    "The server returned an incompatible segmented range.");
            }

            await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var state = getState();
                var responseEntityTag = response.Headers.ETag?.ToString();
                if (!string.IsNullOrWhiteSpace(state.EntityTag) &&
                    !string.IsNullOrWhiteSpace(responseEntityTag) &&
                    !state.EntityTag.Equals(responseEntityTag, StringComparison.Ordinal))
                {
                    return FailureBool(
                        "Download.RemoteChanged",
                        "The remote media validator changed between segmented ranges.");
                }

                if (string.IsNullOrWhiteSpace(state.EntityTag) && !string.IsNullOrWhiteSpace(responseEntityTag))
                {
                    setState(state with { EntityTag = responseEntityTag });
                }
            }
            finally
            {
                stateGate.Release();
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            var received = 0L;
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

                    if (received + count > segment.Length)
                    {
                        return FailureBool(
                            "Download.RemoteChanged",
                            "The server sent more bytes than the requested segment.");
                    }

                    await RandomAccess.WriteAsync(
                        output,
                        buffer.AsMemory(0, count),
                        segment.Start + received,
                        cancellationToken).ConfigureAwait(false);
                    received += count;
                    reportBytes(count);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (received != segment.Length)
            {
                return FailureBool(
                    "Download.Incomplete",
                    "The server ended a segmented range early.",
                    isTransient: true);
            }

            await stateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var state = getState();
                var completed = state.Completed.ToArray();
                completed[segment.Index] = true;
                var updated = state with { Completed = completed };
                await SegmentedDownloadStateStore.WriteAsync(statePath, updated, cancellationToken)
                    .ConfigureAwait(false);
                setState(updated);
            }
            finally
            {
                stateGate.Release();
            }

            return Result<bool>.Success(true);
        }
        catch (OperationCanceledException)
        {
            return FailureBool("Operation.Cancelled", "The download was cancelled.");
        }
        catch (HttpRequestException exception)
        {
            return FailureBool(
                "Network.RequestFailed",
                "The media connection failed.",
                exception.GetType().Name,
                isTransient: true);
        }
        catch (IOException exception)
        {
            return FailureBool(
                "Download.TransferFailed",
                "A segmented range transfer failed.",
                exception.GetType().Name,
                isTransient: true);
        }
    }

    private static bool IsCompatible(
        SegmentedDownloadState? state,
        DownloadRequest request,
        IReadOnlyList<ByteRange> segments) =>
        state is not null &&
        state.SchemaVersion == SegmentedDownloadState.CurrentSchemaVersion &&
        string.Equals(state.SourceIdentity, request.SourceIdentity, StringComparison.Ordinal) &&
        state.ExpectedLength == request.ExpectedLength &&
        state.SegmentCount == segments.Count &&
        state.Completed is not null &&
        state.Completed.Length == segments.Count;

    private static ByteRange[] CreateSegments(long totalLength, int count)
    {
        count = checked((int)Math.Min(totalLength, count));
        var baseLength = totalLength / count;
        var remainder = totalLength % count;
        var ranges = new ByteRange[count];
        var start = 0L;
        for (var index = 0; index < count; index++)
        {
            var length = baseLength + (index < remainder ? 1 : 0);
            ranges[index] = new ByteRange(index, start, checked(start + length - 1));
            start += length;
        }

        return ranges;
    }

    private static DownloadProgress Progress(
        long received,
        long total,
        long attemptBytes,
        TimeSpan elapsed)
    {
        var speed = elapsed.TotalSeconds > 0 ? attemptBytes / elapsed.TotalSeconds : 0;
        var remaining = speed > 0
            ? TimeSpan.FromSeconds(Math.Max(0, total - received) / speed)
            : (TimeSpan?)null;
        return new DownloadProgress(received, total, speed, remaining);
    }

    private static string StatePath(string destination) => destination + ".part.segments.json";

    private static Result<DownloadReceipt> Failure(
        string code,
        string message,
        string? detail = null,
        bool isTransient = false) =>
        Result<DownloadReceipt>.Failure(new TubeForgeError(code, message, detail, isTransient));

    private static Result<bool> FailureBool(
        string code,
        string message,
        string? detail = null,
        bool isTransient = false) =>
        Result<bool>.Failure(new TubeForgeError(code, message, detail, isTransient));

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ByteRange(int Index, long Start, long End)
    {
        public long Length => checked(End - Start + 1);
    }
}

internal sealed record SegmentedDownloadState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public required string SourceIdentity { get; init; }

    public required long ExpectedLength { get; init; }

    public required int SegmentCount { get; init; }

    public required bool[] Completed { get; init; }

    public string? EntityTag { get; init; }
}

internal static class SegmentedDownloadStateStore
{
    private const long MaximumStateBytes = 64 * 1024;
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        MaxDepth = 8,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static async Task<SegmentedDownloadState?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path) || new FileInfo(path).Length > MaximumStateBytes)
        {
            return null;
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            8 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<SegmentedDownloadState>(
            stream,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task WriteAsync(
        string path,
        SegmentedDownloadState state,
        CancellationToken cancellationToken)
    {
        var pendingPath = path + ".new";
        await using (var stream = new FileStream(
                         pendingPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         8 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, state, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            stream.Flush(flushToDisk: true);
        }

        File.Move(pendingPath, path, overwrite: true);
    }

    public static long? ReadCompletedBytes(string destinationPath)
    {
        try
        {
            var destination = Path.GetFullPath(destinationPath);
            var partialPath = destination + ".part";
            var statePath = destination + ".part.segments.json";
            if (!File.Exists(partialPath) || !File.Exists(statePath) ||
                new FileInfo(statePath).Length > MaximumStateBytes)
            {
                return null;
            }

            var state = JsonSerializer.Deserialize<SegmentedDownloadState>(
                File.ReadAllText(statePath),
                SerializerOptions);
            if (state is null ||
                state.SchemaVersion != SegmentedDownloadState.CurrentSchemaVersion ||
                state.ExpectedLength <= 0 ||
                state.SegmentCount is < 2 or > 8 ||
                state.Completed is null ||
                state.Completed.Length != state.SegmentCount ||
                new FileInfo(partialPath).Length != state.ExpectedLength)
            {
                return null;
            }

            var baseLength = state.ExpectedLength / state.SegmentCount;
            var remainder = state.ExpectedLength % state.SegmentCount;
            var completedBytes = 0L;
            for (var index = 0; index < state.SegmentCount; index++)
            {
                if (state.Completed[index])
                {
                    completedBytes = checked(completedBytes + baseLength + (index < remainder ? 1 : 0));
                }
            }

            return completedBytes;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or
                                          ArgumentException or NotSupportedException or OverflowException)
        {
            return null;
        }
    }
}
