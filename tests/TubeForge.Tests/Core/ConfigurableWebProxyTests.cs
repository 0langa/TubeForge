using System.Net;
using TubeForge.Core.Networking;
using TubeForge.Core.Settings;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class ConfigurableWebProxyTests
{
    [Test]
    public static void SwitchesBetweenSystemManualAndNoProxyWithoutCredentials()
    {
        var destination = new Uri("https://www.youtube.com/watch");
        var systemEndpoint = new Uri("http://system-proxy.test:8080/");
        var manualEndpoint = new Uri("https://manual-proxy.test:8443/");
        var proxy = new ConfigurableWebProxy(
            new NetworkProxyConfiguration(NetworkProxyMode.System),
            new FixedProxy(systemEndpoint));

        Assert.Equal(systemEndpoint, proxy.GetProxy(destination));
        Assert.False(proxy.IsBypassed(destination));

        proxy.Update(new NetworkProxyConfiguration(NetworkProxyMode.Manual, manualEndpoint));
        Assert.Equal(manualEndpoint, proxy.GetProxy(destination));
        Assert.False(proxy.IsBypassed(destination));

        proxy.Update(new NetworkProxyConfiguration(NetworkProxyMode.None));
        Assert.Equal(destination, proxy.GetProxy(destination));
        Assert.True(proxy.IsBypassed(destination));
        Assert.True(proxy.Credentials is null);
        Assert.Throws<NotSupportedException>(() => proxy.Credentials = CredentialCache.DefaultCredentials);
    }

    [Test]
    public static void RejectsCredentialsPathsQueriesAndUnsupportedSchemes()
    {
        foreach (var value in new[]
                 {
                     "", "127.0.0.1:8080", "socks5://127.0.0.1:1080/",
                     "http://user:secret@127.0.0.1:8080/", "http://127.0.0.1:8080/path",
                     "http://127.0.0.1:8080/?token=secret", "http://127.0.0.1:8080/#fragment"
                 })
        {
            Assert.False(NetworkProxyPolicy.TryParseManualUri(value, out _), value);
        }

        Assert.True(NetworkProxyPolicy.TryParseManualUri("http://127.0.0.1:8080/", out _));
        Assert.True(NetworkProxyPolicy.TryParseManualUri("https://proxy.example:8443/", out _));
    }

    private sealed class FixedProxy(Uri endpoint) : IWebProxy
    {
        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => endpoint;

        public bool IsBypassed(Uri host) => false;
    }
}
