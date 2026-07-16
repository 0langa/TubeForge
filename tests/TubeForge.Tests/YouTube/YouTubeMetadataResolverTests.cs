using System.Net;
using System.Net.Http.Headers;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.Tests.Framework;
using TubeForge.YouTube;

namespace TubeForge.Tests.YouTube;

public static class YouTubeMetadataResolverTests
{
    [Test]
    public static async Task FetchesExpectedWatchPageAndMapsIt()
    {
        var html = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "watch-page-basic.html"));
        using var handler = new StubHandler(request =>
        {
            Assert.Equal("www.youtube.com", request.RequestUri?.Host);
            Assert.Equal("Fixture123_", QueryValue(request.RequestUri!, "v"));
            Assert.True(request.Headers.UserAgent.Count > 0);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html)
            };
        });
        using var client = new HttpClient(handler);
        var resolver = new YouTubeMetadataResolver(client);

        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));
        var result = await resolver.ResolveAsync(videoId);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("Fixture {video}; title", result.Value.Metadata.Title);
    }

    [Test]
    public static async Task ClassifiesRateLimitWithoutReadingBody()
    {
        using var handler = new StubHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(17));
            return response;
        });
        using var client = new HttpClient(handler);
        var resolver = new YouTubeMetadataResolver(client);
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));

        var result = await resolver.ResolveAsync(videoId);

        Assert.False(result.IsSuccess);
        Assert.Equal("Network.RateLimited", result.Error?.Code);
        Assert.True(result.Error?.IsTransient == true);
        Assert.Equal(TimeSpan.FromSeconds(17), result.Error?.RetryAfter);
    }

    [Test]
    public static async Task FetchesPlayerScriptAndDeciphersCipheredFormat()
    {
        const string watchPage = """
            <script>
            var ytInitialPlayerResponse={
              "playabilityStatus":{"status":"OK"},
              "videoDetails":{"videoId":"Fixture123_","title":"Cipher fixture","lengthSeconds":"10"},
              "streamingData":{"formats":[{
                "itag":22,
                "signatureCipher":"url=https%3A%2F%2Ffixture.googlevideo.com%2Fvideoplayback%3Fitag%3D22&s=abcdef&sp=sig",
                "mimeType":"video/mp4; codecs=\"avc1.64001F, mp4a.40.2\"",
                "width":1280,"height":720,"contentLength":"10"
              }]}
            };
            ytcfg.set({"PLAYER_JS_URL":"/s/player/resolver-fixture/base.js"});
            </script>
            """;
        const string playerScript = """
            var AB={rv:function(a){a.reverse()},sl:function(a,b){a.splice(0,b)},sw:function(a,b){var c=a[0];a[0]=a[b%a.length];a[b%a.length]=c}};
            XY=function(a){a=a.split("");AB.sw(a,2);AB.rv(a);AB.sl(a,1);return a.join("")};
            """;
        var requestCount = 0;
        using var handler = new StubHandler(request =>
        {
            requestCount++;
            if (request.RequestUri?.Host == "www.youtube.com" && request.RequestUri.AbsolutePath == "/watch")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(watchPage) };
            }

            if (request.RequestUri?.Host == "www.youtube.com" &&
                request.RequestUri.AbsolutePath == "/s/player/resolver-fixture/base.js")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(playerScript) };
            }

            Assert.Equal("fixture.googlevideo.com", request.RequestUri?.Host);
            Assert.Equal(0L, request.Headers.Range?.Ranges.Single().From);
            var probe = new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([0])
            };
            probe.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 0, 10);
            return probe;
        });
        using var client = new HttpClient(handler);
        var resolver = new YouTubeMetadataResolver(client);
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));

        var result = await resolver.ResolveAsync(videoId);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, result.Value.Metadata.Formats.Count);
        Assert.Equal(22, result.Value.Metadata.Formats[0].FormatId);
        Assert.True(result.Value.Metadata.Formats[0].Url.Query.Contains("sig=edabc", StringComparison.Ordinal));
        Assert.Equal(3, requestCount);
    }

    [Test]
    public static async Task FetchesPlayerScriptAndResolvesThrottledDirectFormat()
    {
        const string watchPage = """
            <script>
            var ytInitialPlayerResponse={
              "playabilityStatus":{"status":"OK"},
              "videoDetails":{"videoId":"Fixture123_","title":"Throttle fixture","lengthSeconds":"10"},
              "streamingData":{"formats":[{
                "itag":22,
                "url":"https://fixture.googlevideo.com/videoplayback?n=abcdef&itag=22",
                "mimeType":"video/mp4; codecs=\"avc1.64001F, mp4a.40.2\"",
                "width":1280,"height":720,"contentLength":"10"
              }]}
            };
            ytcfg.set({"PLAYER_JS_URL":"/s/player/throttle-fixture/base.js"});
            </script>
            """;
        const string playerScript = """
            const OP={rv(a){a.reverse()},sl(a,b){a.splice(0,b)}};
            const NT=(a)=>{a=a.split('');OP.sl(a,2);OP.rv(a);return a.join('')};
            const TF=[NT];let n=url.searchParams.get('n');n=TF[0](n);url.searchParams.set('n',n);
            """;
        var requestCount = 0;
        using var handler = new StubHandler(request =>
        {
            requestCount++;
            if (request.RequestUri?.AbsolutePath == "/watch")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(watchPage) };
            }

            if (request.RequestUri?.AbsolutePath == "/s/player/throttle-fixture/base.js")
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(playerScript) };
            }

            Assert.Equal("fixture.googlevideo.com", request.RequestUri?.Host);
            Assert.Equal("fedc", QueryValue(request.RequestUri!, "n"));
            return new HttpResponseMessage(HttpStatusCode.PartialContent)
            {
                Content = new ByteArrayContent([0])
            };
        });
        using var client = new HttpClient(handler);
        var resolver = new YouTubeMetadataResolver(client);
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));

        var result = await resolver.ResolveAsync(videoId);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal("fedc", QueryValue(result.Value.Metadata.Formats.Single().Url, "n"));
        Assert.Equal(3, requestCount);
    }

    [Test]
    public static async Task UsesVersionedAndroidClientWhenPageHasNoDirectFormats()
    {
        const string watchPage = """
            <script>
            var ytInitialPlayerResponse={
              "playabilityStatus":{"status":"OK"},
              "videoDetails":{"videoId":"Fixture123_","title":"Watch metadata","lengthSeconds":"10","isShortsEligible":true},
              "captions":{"playerCaptionsTracklistRenderer":{"captionTracks":[{
                "baseUrl":"https://www.youtube.com/api/timedtext?v=Fixture123_&lang=en",
                "name":{"simpleText":"English"},
                "vssId":".en","languageCode":"en","isTranslatable":true
              }]}}
            };
            ytcfg.set({
              "INNERTUBE_API_KEY":"fixturePublicConfig",
              "VISITOR_DATA":"fixtureVisitor",
              "PLAYER_JS_URL":"/s/player/android-fixture/base.js"
            });
            </script>
            """;
        const string playerResponse = """
            {
              "playabilityStatus":{"status":"OK"},
              "videoDetails":{"videoId":"Fixture123_","title":"Android metadata","lengthSeconds":"10"},
              "streamingData":{"formats":[{
                "itag":22,
                "url":"https://fixture.googlevideo.com/videoplayback?id=synthetic&itag=22",
                "mimeType":"video/mp4; codecs=\"avc1.64001F, mp4a.40.2\"",
                "width":1280,"height":720,"contentLength":"10"
              }]}
            }
            """;
        var requestCount = 0;
        using var handler = new StubHandler(request =>
        {
            requestCount++;
            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(watchPage) };
            }

            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/youtubei/v1/player", request.RequestUri?.AbsolutePath);
            Assert.True(request.Headers.Contains("X-YouTube-Client-Name"));
            Assert.True(request.Headers.Contains("X-YouTube-Client-Version"));
            Assert.Equal("application/json", request.Content?.Headers.ContentType?.MediaType);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(playerResponse) };
        });
        using var client = new HttpClient(handler);
        var resolver = new YouTubeMetadataResolver(client);
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));

        var result = await resolver.ResolveAsync(videoId);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(1, result.Value.Metadata.Formats.Count);
        Assert.Equal(1, result.Value.Metadata.CaptionTracks.Count);
        Assert.Equal("en", result.Value.Metadata.CaptionTracks[0].LanguageCode);
        Assert.Equal("Android metadata", result.Value.Metadata.Title);
        Assert.Equal(VideoContentKind.Short, result.Value.Metadata.ContentKind);
        Assert.Equal("AndroidClientResolved", result.Value.Diagnostics?.Stage);
        Assert.Equal(2, requestCount);
    }

    private static string? QueryValue(Uri uri, string key)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&'))
        {
            var components = pair.Split('=', 2);
            if (Uri.UnescapeDataString(components[0]).Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return components.Length == 2 ? Uri.UnescapeDataString(components[1]) : string.Empty;
            }
        }

        return null;
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
