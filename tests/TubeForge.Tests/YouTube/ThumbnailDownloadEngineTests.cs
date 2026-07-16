using System.Net;
using System.Net.Http.Headers;
using TubeForge.Tests.Framework;
using TubeForge.YouTube.Sidecars;

namespace TubeForge.Tests.YouTube;

public static class ThumbnailDownloadEngineTests
{
    [Test]
    public static async Task DownloadsValidatedImageAtomically()
    {
        using var directory = new TestDirectory();
        var destination = Path.Combine(directory.Path, "thumbnail.jpg");
        var payload = new byte[] { 0xFF, 0xD8, 0xFF, 0xDB, 0x00, 0x43, 0xFF, 0xD9 };
        using var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var engine = new ThumbnailDownloadEngine(client);

        var result = await engine.DownloadAsync(new ThumbnailDownloadRequest
        {
            SourceUrl = new Uri("https://i.ytimg.com/vi/Fixture123_/maxresdefault.jpg"),
            DestinationPath = destination
        });

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("image/jpeg", result.Value.MediaType);
        Assert.Equal(payload.LongLength, result.Value.BytesWritten);
        Assert.True((await File.ReadAllBytesAsync(destination)).SequenceEqual(payload));
        Assert.False(File.Exists(destination + ".part"));
    }

    [Test]
    public static async Task RejectsUnsafeSourcesRedirectsAndDisguisedContent()
    {
        using var directory = new TestDirectory();
        var requestCount = 0;
        using var handler = new StubHandler((request, _) =>
        {
            requestCount++;
            if (request.RequestUri!.AbsolutePath.Contains("redirect", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
                {
                    Headers = { Location = new Uri("https://evil.invalid/stolen.jpg") }
                });
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("not an image"u8.ToArray())
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var engine = new ThumbnailDownloadEngine(client);

        var unsafeSource = await engine.DownloadAsync(new ThumbnailDownloadRequest
        {
            SourceUrl = new Uri("https://ytimg.com.evil.invalid/thumbnail.jpg"),
            DestinationPath = Path.Combine(directory.Path, "unsafe.jpg")
        });
        var unsafeRedirect = await engine.DownloadAsync(new ThumbnailDownloadRequest
        {
            SourceUrl = new Uri("https://i.ytimg.com/redirect.jpg"),
            DestinationPath = Path.Combine(directory.Path, "redirect.jpg")
        });
        var disguised = await engine.DownloadAsync(new ThumbnailDownloadRequest
        {
            SourceUrl = new Uri("https://i.ytimg.com/disguised.jpg"),
            DestinationPath = Path.Combine(directory.Path, "disguised.jpg")
        });

        Assert.False(unsafeSource.IsSuccess);
        Assert.Equal("Thumbnail.UnsafeSource", unsafeSource.Error?.Code);
        Assert.Equal("Thumbnail.UnsafeRedirect", unsafeRedirect.Error?.Code);
        Assert.Equal("Thumbnail.InvalidImage", disguised.Error?.Code);
        Assert.Equal(2, requestCount);
        Assert.False(Directory.EnumerateFiles(directory.Path, "*.part").Any());
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
