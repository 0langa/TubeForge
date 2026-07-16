using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Diagnostics;
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

    private static HttpResponseMessage Response(HttpStatusCode status, byte[] content) => new(status)
    {
        Content = new ByteArrayContent(content)
    };

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
