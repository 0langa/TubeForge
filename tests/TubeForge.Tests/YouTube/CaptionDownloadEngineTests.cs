using System.Net;
using System.Text;
using TubeForge.Tests.Framework;
using TubeForge.YouTube.Captions;

namespace TubeForge.Tests.YouTube;

public static class CaptionDownloadEngineTests
{
    [Test]
    public static async Task FetchesWebVttAndAtomicallyWritesSubRip()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "caption.srt");
        Uri? requested = null;
        using var handler = new StubHandler((request, _) =>
        {
            requested = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("WEBVTT\n\n00:01.000 --> 00:02.000\nHello\n", Encoding.UTF8)
            });
        });
        using var client = new HttpClient(handler);
        var engine = new CaptionDownloadEngine(client);

        var result = await engine.DownloadAsync(new CaptionDownloadRequest
        {
            SourceUrl = new Uri("https://www.youtube.com/api/timedtext?v=Fixture123_&lang=en"),
            DestinationPath = destination,
            OutputFormat = CaptionOutputFormat.SubRip
        });

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, result.Value.CueCount);
        Assert.True(requested!.Query.Contains("fmt=vtt", StringComparison.Ordinal));
        Assert.True((await File.ReadAllTextAsync(destination)).Contains(
            "00:00:01,000 --> 00:00:02,000",
            StringComparison.Ordinal));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Test]
    public static async Task RejectsUnsafeSourceAndRedirect()
    {
        using var directory = new TestDirectory();
        using var handler = new StubHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.invalid/api/timedtext"),
                Content = new StringContent("WEBVTT\n")
            }));
        using var client = new HttpClient(handler);
        var engine = new CaptionDownloadEngine(client);
        var unsafeSource = await engine.DownloadAsync(new CaptionDownloadRequest
        {
            SourceUrl = new Uri("https://youtube.com.evil.invalid/api/timedtext"),
            DestinationPath = Path.Combine(directory.Path, "unsafe.vtt"),
            OutputFormat = CaptionOutputFormat.WebVtt
        });
        var unsafeRedirect = await engine.DownloadAsync(new CaptionDownloadRequest
        {
            SourceUrl = new Uri("https://www.youtube.com/api/timedtext?v=Fixture123_&lang=en"),
            DestinationPath = Path.Combine(directory.Path, "redirect.vtt"),
            OutputFormat = CaptionOutputFormat.WebVtt
        });

        Assert.False(unsafeSource.IsSuccess);
        Assert.Equal("Caption.UnsafeSource", unsafeSource.Error?.Code);
        Assert.False(unsafeRedirect.IsSuccess);
        Assert.Equal("Caption.UnsafeRedirect", unsafeRedirect.Error?.Code);
    }

    [Test]
    public static async Task RejectsOversizedResponseBeforeWritingPartialFile()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "oversized.vtt");
        using var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1])
            };
            response.Content.Headers.ContentLength = 11 * 1024 * 1024;
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var engine = new CaptionDownloadEngine(client);

        var result = await engine.DownloadAsync(new CaptionDownloadRequest
        {
            SourceUrl = new Uri("https://www.youtube.com/api/timedtext?v=Fixture123_&lang=en"),
            DestinationPath = destination,
            OutputFormat = CaptionOutputFormat.WebVtt
        });

        Assert.False(result.IsSuccess);
        Assert.Equal("Caption.TooLarge", result.Error?.Code);
        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".part"));
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
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
