using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TubeForge.Tests.Downloads;

internal sealed class LoopbackHttpResponseServer : IAsyncDisposable
{
    private const int MaximumRequestBytes = 16 * 1024;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TcpListener _listener;
    private readonly byte[] _payload;
    private readonly int _maximumRequests;
    private readonly TaskCompletionSource<string> _request = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _serverTask;

    private LoopbackHttpResponseServer(IPAddress address, byte[] payload, int maximumRequests)
    {
        _payload = payload;
        _maximumRequests = maximumRequests;
        _listener = new TcpListener(address, 0);
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        EndpointUri = new UriBuilder(Uri.UriSchemeHttp, endpoint.Address.ToString(), endpoint.Port, "/media").Uri;
        _serverTask = RunAsync(_cancellation.Token);
    }

    public Uri EndpointUri { get; }

    public Task<string> Request => _request.Task;

    public static LoopbackHttpResponseServer Start(
        IPAddress address,
        byte[] payload,
        int maximumRequests = 1)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(payload);
        if (!IPAddress.IsLoopback(address))
        {
            throw new ArgumentException("The test server must bind to loopback.", nameof(address));
        }

        if (maximumRequests is < 1 or > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumRequests));
        }

        return new LoopbackHttpResponseServer(address, payload.ToArray(), maximumRequests);
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
        for (var requestIndex = 0; requestIndex < _maximumRequests; requestIndex++)
        {
            using var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            client.NoDelay = true;
            await using var stream = client.GetStream();
            var request = await ReadRequestAsync(stream, cancellationToken).ConfigureAwait(false);
            _request.TrySetResult(request);
            var headers = Encoding.ASCII.GetBytes(
                "HTTP/1.1 200 OK\r\n" +
                $"Content-Length: {_payload.Length}\r\n" +
                "Content-Type: application/octet-stream\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(headers, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(_payload, cancellationToken).ConfigureAwait(false);
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
}
