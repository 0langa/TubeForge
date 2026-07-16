using System.Net;
using System.Net.Http.Headers;
using TubeForge.Core.Networking;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class HttpRetryAfterParserTests
{
    [Test]
    public static void ParsesDeltaAndDateFormsWithSafeBounds()
    {
        using var deltaResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        deltaResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
        Assert.Equal(TimeSpan.FromSeconds(12), HttpRetryAfterParser.Parse(deltaResponse.Headers));

        var now = new DateTimeOffset(2026, 7, 16, 20, 0, 0, TimeSpan.Zero);
        using var dateResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        dateResponse.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(30), HttpRetryAfterParser.Parse(dateResponse.Headers, now));

        using var excessiveResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        excessiveResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromHours(1));
        Assert.Equal(HttpRetryAfterParser.MaximumDelay, HttpRetryAfterParser.Parse(excessiveResponse.Headers));
    }

    [Test]
    public static void RejectsMissingExpiredAndZeroValues()
    {
        var now = new DateTimeOffset(2026, 7, 16, 20, 0, 0, TimeSpan.Zero);
        using var missing = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        Assert.Equal<TimeSpan?>(null, HttpRetryAfterParser.Parse(missing.Headers, now));

        using var expired = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        expired.Headers.RetryAfter = new RetryConditionHeaderValue(now.AddSeconds(-1));
        Assert.Equal<TimeSpan?>(null, HttpRetryAfterParser.Parse(expired.Headers, now));

        using var zero = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        zero.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        Assert.Equal<TimeSpan?>(null, HttpRetryAfterParser.Parse(zero.Headers, now));
    }
}
