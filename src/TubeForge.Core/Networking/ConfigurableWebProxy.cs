using System.Net;
using TubeForge.Core.Settings;

namespace TubeForge.Core.Networking;

public sealed record NetworkProxyConfiguration(NetworkProxyMode Mode, Uri? ManualProxyUri = null)
{
    public static NetworkProxyConfiguration FromSettings(TubeForgeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ProxyMode != NetworkProxyMode.Manual)
        {
            return new NetworkProxyConfiguration(settings.ProxyMode);
        }

        if (!NetworkProxyPolicy.TryParseManualUri(settings.ManualProxyUri, out var uri))
        {
            throw new ArgumentException("The manual proxy URI is invalid.", nameof(settings));
        }

        return new NetworkProxyConfiguration(settings.ProxyMode, uri);
    }
}

public static class NetworkProxyPolicy
{
    public const int MaximumUriLength = 2_048;

    public static bool TryParseManualUri(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaximumUriLength ||
            value.Any(char.IsControl) || !Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            candidate.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(candidate.IdnHost) ||
            !string.IsNullOrEmpty(candidate.UserInfo) || !string.IsNullOrEmpty(candidate.Query) ||
            !string.IsNullOrEmpty(candidate.Fragment) || candidate.AbsolutePath != "/")
        {
            return false;
        }

        uri = candidate;
        return true;
    }
}

public sealed class ConfigurableWebProxy : IWebProxy
{
    private readonly IWebProxy _systemProxy;
    private NetworkProxyConfiguration _configuration;

    public ConfigurableWebProxy(
        NetworkProxyConfiguration configuration,
        IWebProxy? systemProxy = null)
    {
        _systemProxy = systemProxy ?? HttpClient.DefaultProxy;
        _configuration = Validate(configuration);
    }

    public NetworkProxyMode Mode => Volatile.Read(ref _configuration).Mode;

    public ICredentials? Credentials
    {
        get => null;
        set
        {
            if (value is not null)
            {
                throw new NotSupportedException("Proxy credentials require a secure credential store.");
            }
        }
    }

    public Uri GetProxy(Uri destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var configuration = Volatile.Read(ref _configuration);
        return configuration.Mode switch
        {
            NetworkProxyMode.None => destination,
            NetworkProxyMode.Manual => configuration.ManualProxyUri!,
            _ => _systemProxy.GetProxy(destination) ?? destination
        };
    }

    public bool IsBypassed(Uri host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var configuration = Volatile.Read(ref _configuration);
        return configuration.Mode switch
        {
            NetworkProxyMode.None => true,
            NetworkProxyMode.Manual => false,
            _ => _systemProxy.IsBypassed(host)
        };
    }

    public void Update(NetworkProxyConfiguration configuration) =>
        Volatile.Write(ref _configuration, Validate(configuration));

    private static NetworkProxyConfiguration Validate(NetworkProxyConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!Enum.IsDefined(configuration.Mode) ||
            configuration.Mode == NetworkProxyMode.Manual &&
            (configuration.ManualProxyUri is null ||
             !NetworkProxyPolicy.TryParseManualUri(configuration.ManualProxyUri.AbsoluteUri, out _)) ||
            configuration.Mode != NetworkProxyMode.Manual && configuration.ManualProxyUri is not null)
        {
            throw new ArgumentException("The proxy configuration is invalid.", nameof(configuration));
        }

        return configuration;
    }
}
