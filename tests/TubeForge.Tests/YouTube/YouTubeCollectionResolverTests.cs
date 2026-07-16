using System.Net;
using System.Net.Http.Headers;
using TubeForge.Core.YouTube;
using TubeForge.Tests.Framework;
using TubeForge.YouTube.Collections;

namespace TubeForge.Tests.YouTube;

public static class YouTubeCollectionResolverTests
{
    [Test]
    public static async Task EnumeratesContinuationPagesAndDeduplicatesVideos()
    {
        var requests = new List<(HttpMethod Method, Uri Uri, string Body)>();
        using var handler = new StubHandler(async (request, cancellationToken) =>
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            requests.Add((request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    request.Method == HttpMethod.Get
                        ? CollectionFixtures.InitialHtml
                        : CollectionFixtures.ContinuationJson)
            };
        });
        using var client = new HttpClient(handler);
        var resolver = new YouTubeCollectionResolver(client);
        var source = YouTubeCollectionUrlParser.Parse(
            "https://www.youtube.com/playlist?list=PL1234567890").Value;

        var result = await resolver.ResolveAsync(source);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.Equal("Fixture playlist", result.Value.Title);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Equal("Video000001", result.Value.Items[0].VideoId.Value);
        Assert.Equal("Video000002", result.Value.Items[1].VideoId.Value);
        Assert.Equal(2, result.Value.Items[1].Index);
        Assert.Equal(2, result.Value.PagesRead);
        Assert.False(result.Value.IsTruncated);
        Assert.Equal(2, requests.Count);
        Assert.Equal(HttpMethod.Post, requests[1].Method);
        Assert.Equal("www.youtube.com", requests[1].Uri.Host);
        Assert.True(requests[1].Body.Contains("ContinuationFixture_1", StringComparison.Ordinal));
    }

    [Test]
    public static async Task StopsAtCallerItemLimitWithoutFetchingContinuation()
    {
        var requestCount = 0;
        using var handler = new StubHandler((_, _) =>
        {
            requestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(CollectionFixtures.InitialHtml)
            });
        });
        using var client = new HttpClient(handler);
        var resolver = new YouTubeCollectionResolver(client);
        var source = YouTubeCollectionUrlParser.Parse(
            "https://www.youtube.com/playlist?list=PL1234567890").Value;

        var result = await resolver.ResolveAsync(source, maximumItems: 1);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.Equal(1, result.Value.Items.Count);
        Assert.True(result.Value.IsTruncated);
        Assert.Equal(1, requestCount);
    }

    [Test]
    public static async Task PreservesBoundedRetryAfterOnRateLimit()
    {
        using var handler = new StubHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(23));
            return Task.FromResult(response);
        });
        using var client = new HttpClient(handler);
        var resolver = new YouTubeCollectionResolver(client);
        var source = YouTubeCollectionUrlParser.Parse(
            "https://www.youtube.com/playlist?list=PL1234567890").Value;

        var result = await resolver.ResolveAsync(source);

        Assert.False(result.IsSuccess);
        Assert.Equal("Network.RateLimited", result.Error?.Code);
        Assert.Equal(TimeSpan.FromSeconds(23), result.Error?.RetryAfter);
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => responseFactory(request, cancellationToken);
    }
}
