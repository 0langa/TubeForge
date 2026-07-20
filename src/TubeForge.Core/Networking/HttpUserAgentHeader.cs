namespace TubeForge.Core.Networking;

public static class HttpUserAgentHeader
{
    public const string DefaultBrowser =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36";

    private const int MaximumLength = 512;

    public static bool TryApply(HttpRequestMessage request, string? userAgent)
    {
        ArgumentNullException.ThrowIfNull(request);

        var value = string.IsNullOrWhiteSpace(userAgent) ? DefaultBrowser : userAgent;
        if (value.Length > MaximumLength || value.Any(character => character is '\r' or '\n' or '\0'))
        {
            return false;
        }

        return request.Headers.TryAddWithoutValidation("User-Agent", value);
    }
}
