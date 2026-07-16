using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using TubeForge.Downloads;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class DirectDownloadEngineTests
{
    [Test]
    public static async Task DownloadsToPartialThenAtomicallyFinalizes()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "fixture.mp4");
        var payload = Encoding.ASCII.GetBytes("synthetic-media");
        using var handler = new StubHandler((request, _) =>
        {
            Assert.True(request.Headers.Range is null);
            var response = Response(HttpStatusCode.OK, payload);
            response.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var progress = new InlineProgress<DownloadProgress>();
        var engine = Engine(client);

        var result = await engine.DownloadAsync(Request(destination, payload.Length), progress);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(payload.Length, checked((int)result.Value.BytesWritten));
        Assert.False(result.Value.Resumed);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".part"));
        Assert.False(File.Exists(destination + ".part.json"));
        Assert.Equal(payload.Length, checked((int)progress.Values[^1].BytesReceived));
        Assert.Equal(1d, progress.Values[^1].Fraction);
    }

    [Test]
    public static async Task ResumesCompatiblePartialUsingRangeAndValidator()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "fixture.mp4");
        await File.WriteAllBytesAsync(destination + ".part", Encoding.ASCII.GetBytes("01234"));
        await File.WriteAllTextAsync(destination + ".part.json", """
            {
              "SchemaVersion": 1,
              "SourceIdentity": "Fixture123_:22",
              "ExpectedLength": 10,
              "EntityTag": "\"fixture-v1\"",
              "LastModified": null
            }
            """);

        using var handler = new StubHandler((request, _) =>
        {
            var range = request.Headers.Range?.Ranges.Single();
            Assert.Equal(5L, range?.From);
            Assert.Equal("\"fixture-v1\"", request.Headers.IfRange?.EntityTag?.Tag);
            var response = Response(HttpStatusCode.PartialContent, Encoding.ASCII.GetBytes("56789"));
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(5, 9, 10);
            response.Headers.ETag = new EntityTagHeaderValue("\"fixture-v1\"");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var engine = Engine(client);

        var result = await engine.DownloadAsync(Request(destination, 10));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Resumed);
        Assert.Equal("0123456789", await File.ReadAllTextAsync(destination));
    }

    [Test]
    public static async Task RetriesTransientHttpFailureThenSucceeds()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "fixture.mp4");
        var attempts = 0;
        var delays = new List<TimeSpan>();
        using var handler = new StubHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(attempts == 1
                ? Response(HttpStatusCode.ServiceUnavailable, [])
                : Response(HttpStatusCode.OK, Encoding.ASCII.GetBytes("done")));
        });
        using var client = new HttpClient(handler);
        var engine = new DirectDownloadEngine(
            client,
            DownloadUriPolicy.YouTubeMediaAndLoopback,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await engine.DownloadAsync(Request(destination, 4));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, attempts);
        Assert.Equal(1, delays.Count);
        Assert.True(delays[0] >= TimeSpan.FromMilliseconds(500));
    }

    [Test]
    public static async Task HonorsBoundedRetryAfterBeforeRetryingRateLimit()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "fixture.mp4");
        var attempts = 0;
        var delays = new List<TimeSpan>();
        using var handler = new StubHandler((_, _) =>
        {
            attempts++;
            if (attempts > 1)
            {
                return Task.FromResult(Response(HttpStatusCode.OK, Encoding.ASCII.GetBytes("done")));
            }

            var response = Response(HttpStatusCode.TooManyRequests, []);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(11));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var engine = new DirectDownloadEngine(
            client,
            DownloadUriPolicy.YouTubeMediaAndLoopback,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        var result = await engine.DownloadAsync(Request(destination, 4));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, attempts);
        Assert.SequenceEqual(new[] { TimeSpan.FromSeconds(11) }, delays);
    }

    [Test]
    public static async Task LeavesPartialWhenServerEndsEarly()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "fixture.mp4");
        var attempts = 0;
        using var handler = new StubHandler((_, _) =>
        {
            attempts++;
            var response = Response(HttpStatusCode.OK, Encoding.ASCII.GetBytes("short"));
            response.Content.Headers.ContentLength = 10;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var engine = Engine(client);

        var result = await engine.DownloadAsync(Request(destination, 10));

        Assert.False(result.IsSuccess);
        Assert.Equal("Download.Incomplete", result.Error?.Code);
        Assert.Equal(DownloadRetryPolicy.MaximumAttempts, attempts);
        Assert.False(File.Exists(destination));
        Assert.True(File.Exists(destination + ".part"));
        Assert.Equal(5L, new FileInfo(destination + ".part").Length);
    }

    [Test]
    public static async Task CancellationStopsWithoutRetry()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "fixture.mp4");
        var attempts = 0;
        using var handler = new StubHandler(async (_, cancellationToken) =>
        {
            attempts++;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        });
        using var client = new HttpClient(handler);
        var engine = Engine(client);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var result = await engine.DownloadAsync(Request(destination, 10), cancellationToken: cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Operation.Cancelled", result.Error?.Code);
        Assert.Equal(1, attempts);
    }

    [Test]
    public static async Task RejectsSpoofedMediaHostBeforeRequest()
    {
        using var directory = new TestDirectory();
        var requests = 0;
        using var handler = new StubHandler((_, _) =>
        {
            requests++;
            return Task.FromResult(Response(HttpStatusCode.OK, []));
        });
        using var client = new HttpClient(handler);
        var engine = new DirectDownloadEngine(client);
        var request = Request(Path.Combine(directory.Path, "fixture.mp4"), 1) with
        {
            SourceUrl = new Uri("https://googlevideo.com.evil.test/media")
        };

        var result = await engine.DownloadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Download.UnsafeSource", result.Error?.Code);
        Assert.Equal(0, requests);
    }

    [Test]
    public static async Task ResumesAfterRealSocketDropsMidResponse()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "socket-fixture.mp4");
        var payload = Encoding.ASCII.GetBytes("hello-world");
        await using var server = LoopbackHttpFaultServer.StartTruncatedThenResumable(payload, 5);
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var engine = Engine(client);
        var request = Request(destination, payload.Length) with { SourceUrl = server.MediaUri };

        var result = await engine.DownloadAsync(request);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Resumed);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, server.Requests.Count);
        Assert.True(server.Requests[1].Contains("Range: bytes=5-", StringComparison.OrdinalIgnoreCase));
        Assert.True(server.Requests[1].Contains("If-Range: \"socket-v1\"", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public static async Task RefusesToFinalizePayloadWithInvalidDeclaredContainer()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "invalid.mp4");
        var payload = Encoding.ASCII.GetBytes("not-an-mp4-container");
        using var handler = new StubHandler((_, _) =>
            Task.FromResult(Response(HttpStatusCode.OK, payload)));
        using var client = new HttpClient(handler);
        var engine = Engine(client);
        var request = Request(destination, payload.Length) with
        {
            ExpectedContainer = TubeForge.Core.Media.MediaContainer.Mp4
        };

        var result = await engine.DownloadAsync(request);

        Assert.False(result.IsSuccess);
        Assert.Equal("Media.InvalidStructure", result.Error?.Code);
        Assert.False(File.Exists(destination));
        Assert.True(File.Exists(destination + ".part"));
    }

    [Test]
    public static async Task DownloadsThroughExplicitHttpProxy()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "proxy-fixture.mp4");
        var payload = Encoding.ASCII.GetBytes("proxied-media");
        await using var proxy = LoopbackHttpResponseServer.Start(IPAddress.Loopback, payload);
        using var handler = new SocketsHttpHandler
        {
            Proxy = new WebProxy(proxy.EndpointUri, BypassOnLocal: false),
            UseProxy = true
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var engine = Engine(client);
        var request = Request(destination, payload.Length) with
        {
            SourceUrl = new Uri("http://localhost:49152/media")
        };

        var result = await engine.DownloadAsync(request);
        var proxyRequest = await proxy.Request;

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
        Assert.True(proxyRequest.StartsWith(
            "GET http://localhost:49152/media HTTP/1.1",
            StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public static async Task DownloadsOverIpv4AndIpv6Loopback()
    {
        using var directory = new TestDirectory();
        var payload = Encoding.ASCII.GetBytes("address-family-media");

        await DownloadFromLoopbackAsync(
            IPAddress.Loopback,
            Path.Combine(directory.Path, "ipv4.mp4"),
            payload);

        if (Socket.OSSupportsIPv6)
        {
            await DownloadFromLoopbackAsync(
                IPAddress.IPv6Loopback,
                Path.Combine(directory.Path, "ipv6.mp4"),
                payload);
        }
    }

    [Test]
    public static async Task ContinuesAfterStalledResponseResumes()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "stalled-response.mp4");
        var payload = Enumerable.Range(0, 256 * 1024).Select(index => (byte)(index % 251)).ToArray();
        var stalled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var contentStream = new GatedReadStream(payload, stalled, resume.Task);
        using var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(contentStream)
            };
            response.Content.Headers.ContentLength = payload.Length;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var engine = Engine(client);

        var download = engine.DownloadAsync(Request(destination, payload.Length));
        await stalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(download.IsCompleted);
        resume.SetResult();
        var result = await download;

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
    }

    [Test]
    public static async Task BackpressureFromSlowDestinationDoesNotLoseBytes()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "slow-destination.mp4");
        var payload = Enumerable.Range(0, 256 * 1024).Select(index => (byte)(index % 241)).ToArray();
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler((_, _) =>
            Task.FromResult(Response(HttpStatusCode.OK, payload)));
        using var client = new HttpClient(handler);
        var engine = new DirectDownloadEngine(
            client,
            DownloadUriPolicy.YouTubeMediaAndLoopback,
            (_, _) => Task.CompletedTask,
            (path, append) => new GatedWriteStream(
                new FileStream(
                    path,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.Read,
                    16 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan),
                writeStarted,
                releaseWrite.Task));

        var download = engine.DownloadAsync(Request(destination, payload.Length));
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(download.IsCompleted);
        releaseWrite.SetResult();
        var result = await download;

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
    }

    [Test]
    public static async Task SegmentedTransferUsesConcurrentValidatedRangesWithoutByteLoss()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "segmented.mp4");
        var payload = Enumerable.Range(0, 1024 * 1024).Select(index => (byte)(index % 239)).ToArray();
        var allRangesStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var rangeCount = 0;
        var active = 0;
        var maximumActive = 0;
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            var range = request.Headers.Range?.Ranges.Single();
            Assert.True(range?.From is not null && range.To is not null);
            var currentActive = Interlocked.Increment(ref active);
            UpdateMaximum(ref maximumActive, currentActive);
            if (Interlocked.Increment(ref rangeCount) == 4)
            {
                allRangesStarted.SetResult();
            }

            await allRangesStarted.Task.WaitAsync(cancellationToken);
            Interlocked.Decrement(ref active);
            return RangeResponse(payload, range!.From!.Value, range.To!.Value, "\"segmented-v1\"");
        });
        using var client = new HttpClient(handler);
        var engine = Engine(client);

        var result = await engine.DownloadAsync(SegmentedRequest(destination, payload.Length));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.False(result.Value.Resumed);
        Assert.Equal(4, rangeCount);
        Assert.Equal(4, maximumActive);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".part"));
        Assert.False(File.Exists(destination + ".part.segments.json"));
    }

    [Test]
    public static async Task SegmentedTransferResumesCompletedRangesAfterTransientFailure()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "segmented-resume.mp4");
        var payload = Enumerable.Range(0, 512 * 1024).Select(index => (byte)(index % 233)).ToArray();
        var requestsByStart = new ConcurrentDictionary<long, int>();
        using var handler = new StubHandler((request, _) =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var start = range.From!.Value;
            var requestCount = requestsByStart.AddOrUpdate(start, 1, (_, count) => count + 1);
            if (start > 0 && requestCount == 1)
            {
                return Task.FromResult(Response(HttpStatusCode.ServiceUnavailable, []));
            }

            return Task.FromResult(RangeResponse(
                payload,
                start,
                range.To!.Value,
                "\"segmented-v1\""));
        });
        using var client = new HttpClient(handler);
        var engine = Engine(client);

        var result = await engine.DownloadAsync(SegmentedRequest(destination, payload.Length));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(result.Value.Resumed);
        Assert.Equal(1, requestsByStart[0]);
        Assert.True(requestsByStart.Values.Count(value => value == 2) == 3);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
    }

    [Test]
    public static async Task SegmentedTransferFallsBackWhenRangesAreUnsupported()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "segmented-fallback.mp4");
        var payload = Encoding.ASCII.GetBytes("range-fallback-payload");
        var rangeRequests = 0;
        var directRequests = 0;
        using var handler = new StubHandler((request, _) =>
        {
            if (request.Headers.Range is null)
            {
                Interlocked.Increment(ref directRequests);
            }
            else
            {
                Interlocked.Increment(ref rangeRequests);
            }

            return Task.FromResult(Response(HttpStatusCode.OK, payload));
        });
        using var client = new HttpClient(handler);
        var engine = Engine(client);

        var result = await engine.DownloadAsync(SegmentedRequest(destination, payload.Length));

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(4, rangeRequests);
        Assert.Equal(1, directRequests);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".part.segments.json"));
    }

    [Test]
    public static async Task SegmentedTransferRejectsValidatorMismatchWithoutPublishingOutput()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "segmented-mismatch.mp4");
        var payload = Enumerable.Range(0, 256 * 1024).Select(index => (byte)(index % 229)).ToArray();
        using var handler = new StubHandler((request, _) =>
        {
            var range = request.Headers.Range!.Ranges.Single();
            var tag = range.From == 0 ? "\"version-a\"" : "\"version-b\"";
            return Task.FromResult(RangeResponse(payload, range.From!.Value, range.To!.Value, tag));
        });
        using var client = new HttpClient(handler);
        var engine = Engine(client);

        var result = await engine.DownloadAsync(SegmentedRequest(destination, payload.Length));

        Assert.False(result.IsSuccess);
        Assert.Equal("Download.RemoteChanged", result.Error?.Code);
        Assert.False(File.Exists(destination));
    }

    [Test]
    public static async Task SegmentedProgressCountsCompletedRangesInsteadOfPreallocatedLength()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "segmented-progress.mp4");
        await using (var partial = new FileStream(destination + ".part", FileMode.CreateNew, FileAccess.Write))
        {
            partial.SetLength(100);
        }

        await SegmentedDownloadStateStore.WriteAsync(
            destination + ".part.segments.json",
            new SegmentedDownloadState
            {
                SourceIdentity = "Fixture123_:22",
                ExpectedLength = 100,
                SegmentCount = 4,
                Completed = [true, false, true, false]
            },
            CancellationToken.None);

        Assert.Equal(50L, SegmentedTransferProgress.GetCompletedBytes(destination));
    }

    private static async Task DownloadFromLoopbackAsync(
        IPAddress address,
        string destination,
        byte[] payload)
    {
        await using var server = LoopbackHttpResponseServer.Start(address, payload);
        using var handler = new SocketsHttpHandler { UseProxy = false };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
        var result = await Engine(client).DownloadAsync(Request(destination, payload.Length) with
        {
            SourceUrl = server.EndpointUri
        });

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.SequenceEqual(payload, await File.ReadAllBytesAsync(destination));
        Assert.True((await server.Request).StartsWith("GET /media HTTP/1.1", StringComparison.Ordinal));
    }

    private static DirectDownloadEngine Engine(HttpClient client) => new(
        client,
        DownloadUriPolicy.YouTubeMediaAndLoopback,
        (_, _) => Task.CompletedTask);

    private static DownloadRequest Request(string destination, long expectedLength) => new()
    {
        SourceUrl = new Uri("http://localhost/media"),
        SourceIdentity = "Fixture123_:22",
        DestinationPath = destination,
        ExpectedLength = expectedLength
    };

    private static DownloadRequest SegmentedRequest(string destination, long expectedLength) =>
        Request(destination, expectedLength) with
        {
            EnableSegmentedTransfer = true,
            MaximumSegments = 4,
            SegmentedTransferMinimumBytes = 1
        };

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] content) => new(status)
    {
        Content = new ByteArrayContent(content)
    };

    private static HttpResponseMessage RangeResponse(
        byte[] payload,
        long from,
        long to,
        string entityTag)
    {
        var length = checked((int)(to - from + 1));
        var content = new byte[length];
        Buffer.BlockCopy(payload, checked((int)from), content, 0, length);
        var response = Response(HttpStatusCode.PartialContent, content);
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, payload.Length);
        response.Headers.ETag = new EntityTagHeaderValue(entityTag);
        return response;
    }

    private static void UpdateMaximum(ref int target, int candidate)
    {
        while (true)
        {
            var current = Volatile.Read(ref target);
            if (candidate <= current || Interlocked.CompareExchange(ref target, candidate, current) == current)
            {
                return;
            }
        }
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        public List<T> Values { get; } = [];

        public void Report(T value) => Values.Add(value);
    }

    private sealed class GatedReadStream(
        byte[] payload,
        TaskCompletionSource stalled,
        Task resume) : Stream
    {
        private readonly MemoryStream _inner = new(payload, writable: false);
        private bool _firstRead = true;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_firstRead)
            {
                _firstRead = false;
                return await _inner.ReadAsync(buffer[..Math.Min(buffer.Length, payload.Length / 2)], cancellationToken);
            }

            stalled.TrySetResult();
            await resume.WaitAsync(cancellationToken);
            return await _inner.ReadAsync(buffer, cancellationToken);
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class GatedWriteStream(
        Stream inner,
        TaskCompletionSource writeStarted,
        Task releaseWrite) : Stream
    {
        private bool _firstWrite = true;

        public override bool CanRead => false;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => true;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            inner.FlushAsync(cancellationToken);

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);

        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_firstWrite)
            {
                _firstWrite = false;
                writeStarted.TrySetResult();
                await releaseWrite.WaitAsync(cancellationToken);
            }

            await inner.WriteAsync(buffer, cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests"));

        public TestDirectory()
        {
            Path = System.IO.Path.Combine(SafeRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            if (!resolved.StartsWith(SafeRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to clean a test directory outside the safe root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
