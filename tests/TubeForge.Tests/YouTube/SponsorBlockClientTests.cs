using System.Net;
using System.Security.Cryptography;
using System.Text;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.Tests.Framework;
using TubeForge.YouTube.SponsorBlock;

namespace TubeForge.Tests.YouTube;

public static class SponsorBlockClientTests
{
    [Test]
    public static async Task UsesPrivacyPrefixAndReturnsOnlyMatchingValidatedSegments()
    {
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));
        Uri? requested = null;
        using var handler = new StubHandler(request =>
        {
            requested = request.RequestUri;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    [
                      {"videoID":"Other12345_","segments":[]},
                      {"videoID":"Fixture123_","segments":[
                        {"segment":[5.25,10.5],"category":"sponsor","actionType":"skip","description":""},
                        {"segment":[1,2],"category":"intro","actionType":"skip","description":"Opening"}
                      ]}
                    ]
                    """)
            };
        });
        using var client = new HttpClient(handler);
        var selection = new SponsorBlockSelection(
            SponsorBlockMode.Chapters,
            SponsorBlockCategories.Sponsor | SponsorBlockCategories.Intro);

        var result = await new SponsorBlockClient(
            client,
            new Uri("https://sponsor.test/")).GetSegmentsAsync(videoId, selection);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("intro", result.Value[0].Category);
        Assert.Equal(TimeSpan.FromSeconds(5.25), result.Value[1].Start);
        Assert.True(requested is not null);
        var prefix = Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(videoId.Value)))[..4];
        Assert.Equal($"/api/skipSegments/{prefix}", requested!.AbsolutePath);
        Assert.False(requested.AbsoluteUri.Contains(videoId.Value, StringComparison.Ordinal));
        Assert.True(requested.Query.Contains("trimUUIDs=true", StringComparison.Ordinal));
    }

    [Test]
    public static async Task TreatsNotFoundAsEmptyAndRejectsMalformedRanges()
    {
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));
        var selection = new SponsorBlockSelection(
            SponsorBlockMode.Remove,
            SponsorBlockCategories.Sponsor);
        using var missingHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var missingClient = new HttpClient(missingHandler);
        var missing = await new SponsorBlockClient(
            missingClient,
            new Uri("https://sponsor.test/")).GetSegmentsAsync(videoId, selection);
        Assert.True(missing.IsSuccess, missing.Error?.Message);
        Assert.Equal(0, missing.Value.Count);

        using var invalidHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                [{"videoID":"Fixture123_","segments":[
                  {"segment":[10,5],"category":"sponsor","actionType":"skip","description":""}
                ]}]
                """)
        });
        using var invalidClient = new HttpClient(invalidHandler);
        var invalid = await new SponsorBlockClient(
            invalidClient,
            new Uri("https://sponsor.test/")).GetSegmentsAsync(videoId, selection);
        Assert.False(invalid.IsSuccess);
        Assert.Equal("SponsorBlock.InvalidResponse", invalid.Error?.Code);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> factory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(factory(request));
    }
}
