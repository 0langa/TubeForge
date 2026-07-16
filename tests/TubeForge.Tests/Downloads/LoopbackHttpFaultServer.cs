using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TubeForge.Tests.Downloads;

internal sealed class LoopbackHttpFaultServer : IAsyncDisposable
{
    private const int MaximumRequestBytes = 16 * 1024;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TcpListener _listener;
    private readonly byte[] _payload;
    private readonly int _truncateAfter;
    private readonly List<string> _requests = [];
    private readonly Task _serverTask;

    private LoopbackHttpFaultServer(byte[] payload, int truncateAfter)
    {
        _payload = payload;
        _truncateAfter = truncateAfter;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        MediaUri = new Uri($"http://127.0.0.1:{endpoint.Port}/media");
        _serverTask = RunAsync(_cancellation.Token);
    }

    public Uri MediaUri { get; }

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_requests)
            {
                return _requests.ToArray();
            }
        }
    }

    public static LoopbackHttpFaultServer StartTruncatedThenResumable(
        byte[] payload,
        int truncateAfter)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (truncateAfter <= 0 || truncateAfter >= payload.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(truncateAfter));
        }

        return new LoopbackHttpFaultServer(payload.ToArray(), truncateAfter);
    }

    public async ValueTask DisposeAsync()
    {
        await _cancellation.CancelAsync();
        _listener.Stop();
        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException) when (_cancellation.IsCancellationRequested)
        {
        }

        _cancellation.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        for (var requestNumber = 0; requestNumber < 2; requestNumber++)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            client.NoDelay = true;
            await using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
            lock (_requests)
            {
                _requests.Add(request);
            }

            if (requestNumber == 0)
            {
                await WriteHeadersAsync(
                    stream,
                    HttpStatusCode.OK,
                    _payload.Length,
                    contentRange: null,
                    cancellationToken).ConfigureAwait(false);
                await stream.WriteAsync(
                    _payload.AsMemory(0, _truncateAfter),
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }

            var rangeStart = ReadRangeStart(request);
            if (rangeStart != _truncateAfter)
            {
                await WriteHeadersAsync(
                    stream,
                    HttpStatusCode.RequestedRangeNotSatisfiable,
                    contentLength: 0,
                    contentRange: $"bytes */{_payload.Length}",
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            var rangeOffset = checked((int)rangeStart.Value);
            var remaining = _payload.Length - rangeOffset;
            await WriteHeadersAsync(
                stream,
                HttpStatusCode.PartialContent,
                remaining,
                $"bytes {rangeStart}-{_payload.Length - 1}/{_payload.Length}",
                cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(
                _payload.AsMemory(rangeOffset, remaining),
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<string> ReadRequestAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[1024];
        while (buffer.Length < MaximumRequestBytes)
        {
            var count = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            buffer.Write(chunk, 0, count);
            var request = Encoding.ASCII.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            if (request.Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                return request;
            }
        }

        throw new IOException("Loopback request headers were incomplete or oversized.");
    }

    private static long? ReadRangeStart(string request)
    {
        foreach (var line in request.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            const string prefix = "Range: bytes=";
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[prefix.Length..].Split('-', 2)[0];
            return long.TryParse(value, out var start) ? start : null;
        }

        return null;
    }

    private static async Task WriteHeadersAsync(
        NetworkStream stream,
        HttpStatusCode statusCode,
        long contentLength,
        string? contentRange,
        CancellationToken cancellationToken)
    {
        var reason = statusCode switch
        {
            HttpStatusCode.OK => "OK",
            HttpStatusCode.PartialContent => "Partial Content",
            HttpStatusCode.RequestedRangeNotSatisfiable => "Range Not Satisfiable",
            _ => statusCode.ToString()
        };
        var rangeHeader = contentRange is null ? string.Empty : $"Content-Range: {contentRange}\r\n";
        var headers = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {(int)statusCode} {reason}\r\n" +
            $"Content-Length: {contentLength}\r\n" +
            "Content-Type: application/octet-stream\r\n" +
            "ETag: \"socket-v1\"\r\n" +
            rangeHeader +
            "Connection: close\r\n\r\n");
        await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
    }
}
