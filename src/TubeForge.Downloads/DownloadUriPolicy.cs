namespace TubeForge.Downloads;

public sealed class DownloadUriPolicy
{
    public static DownloadUriPolicy YouTubeMediaOnly { get; } = new();

    public static DownloadUriPolicy YouTubeMediaAndLoopback { get; } = new(allowLoopbackHttp: true);

    private readonly bool _allowLoopbackHttp;

    private DownloadUriPolicy(bool allowLoopbackHttp = false)
    {
        _allowLoopbackHttp = allowLoopbackHttp;
    }

    public bool IsAllowed(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        if (_allowLoopbackHttp &&
            uri.Scheme == Uri.UriSchemeHttp &&
            (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return uri.Scheme == Uri.UriSchemeHttps &&
               (uri.Host.Equals("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase));
    }
}
