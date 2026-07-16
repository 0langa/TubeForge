namespace TubeForge.YouTube.Player;

internal static class ThrottlingUrl
{
    private const int MaximumThrottlingValueLength = 4 * 1024;

    public static bool RequiresTransform(Uri mediaUrl) =>
        IsTrustedMediaUrl(mediaUrl) && TryFindParameter(mediaUrl.Query, "n", out _, out _);

    public static Uri? Resolve(Uri mediaUrl, SignatureTransformPlan plan)
    {
        ArgumentNullException.ThrowIfNull(mediaUrl);
        ArgumentNullException.ThrowIfNull(plan);
        if (!IsTrustedMediaUrl(mediaUrl) ||
            !TryFindParameter(mediaUrl.Query, "n", out var valueStart, out var valueLength))
        {
            return mediaUrl;
        }

        var rawQuery = mediaUrl.Query.TrimStart('?');
        var encodedValue = rawQuery.Substring(valueStart, valueLength);
        var value = Decode(encodedValue);
        if (value.Length is 0 or > MaximumThrottlingValueLength)
        {
            return null;
        }

        var transformed = plan.Apply(value);
        if (transformed.Length == 0)
        {
            return null;
        }

        var builder = new UriBuilder(mediaUrl)
        {
            Query = rawQuery[..valueStart] + Uri.EscapeDataString(transformed) + rawQuery[(valueStart + valueLength)..]
        };
        return builder.Uri;
    }

    private static bool TryFindParameter(
        string query,
        string target,
        out int valueStart,
        out int valueLength)
    {
        valueStart = -1;
        valueLength = 0;
        var raw = query.TrimStart('?');
        var pairStart = 0;
        while (pairStart <= raw.Length)
        {
            var pairEnd = raw.IndexOf('&', pairStart);
            if (pairEnd < 0) pairEnd = raw.Length;
            var equals = raw.IndexOf('=', pairStart, pairEnd - pairStart);
            var keyEnd = equals < 0 ? pairEnd : equals;
            if (string.Equals(Decode(raw[pairStart..keyEnd]), target, StringComparison.Ordinal))
            {
                valueStart = equals < 0 ? pairEnd : equals + 1;
                valueLength = pairEnd - valueStart;
                return true;
            }

            if (pairEnd == raw.Length) break;
            pairStart = pairEnd + 1;
        }

        return false;
    }

    private static bool IsTrustedMediaUrl(Uri mediaUrl) =>
        mediaUrl.IsAbsoluteUri &&
        mediaUrl.Scheme == Uri.UriSchemeHttps &&
        (mediaUrl.Host.Equals("googlevideo.com", StringComparison.OrdinalIgnoreCase) ||
         mediaUrl.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase));

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}
