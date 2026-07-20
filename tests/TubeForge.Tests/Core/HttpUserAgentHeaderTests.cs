using TubeForge.Core.Networking;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class HttpUserAgentHeaderTests
{
    [Test]
    public static void AppliesProviderUserAgentThatContainsAComma()
    {
        const string providerUserAgent =
            "Mozilla/5.0 Cobalt/25.lts (unlike Gecko), Unknown_TV_Unknown_0/Unknown (Unknown, Unknown)";
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://fixture.example/media");

        var applied = HttpUserAgentHeader.TryApply(request, providerUserAgent);

        Assert.True(applied);
        Assert.Equal(providerUserAgent, request.Headers.GetValues("User-Agent").Single());
    }

    [Test]
    public static void RejectsHeaderInjectionAndExcessiveValues()
    {
        using var injected = new HttpRequestMessage(HttpMethod.Get, "https://fixture.example/media");
        Assert.False(HttpUserAgentHeader.TryApply(injected, "fixture/1.0\r\nX-Injected: yes"));
        Assert.False(injected.Headers.Contains("User-Agent"));

        using var excessive = new HttpRequestMessage(HttpMethod.Get, "https://fixture.example/media");
        Assert.False(HttpUserAgentHeader.TryApply(excessive, new string('x', 513)));
        Assert.False(excessive.Headers.Contains("User-Agent"));
    }

    [Test]
    public static void UsesBrowserDefaultForMissingValue()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://fixture.example/media");

        Assert.True(HttpUserAgentHeader.TryApply(request, null));
        Assert.Equal(HttpUserAgentHeader.DefaultBrowser, request.Headers.UserAgent.ToString());
    }
}
