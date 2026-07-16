namespace TubeForge.YouTube.Player;

internal static class SignatureCipherUrl
{
    public static Uri? Resolve(string cipherQuery, SignatureTransformPlan plan)
    {
        ArgumentNullException.ThrowIfNull(cipherQuery);
        ArgumentNullException.ThrowIfNull(plan);
        var values = ParseQuery(cipherQuery);
        if (!values.TryGetValue("url", out var urlText) ||
            !values.TryGetValue("s", out var encryptedSignature) ||
            !Uri.TryCreate(urlText, UriKind.Absolute, out var mediaUrl) ||
            mediaUrl.Scheme != Uri.UriSchemeHttps ||
            (!mediaUrl.Host.Equals("googlevideo.com", StringComparison.OrdinalIgnoreCase) &&
             !mediaUrl.Host.EndsWith(".googlevideo.com", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var signatureParameter = values.GetValueOrDefault("sp", "signature");
        if (signatureParameter.Length is < 1 or > 32 ||
            signatureParameter.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '_'))
        {
            return null;
        }

        var signature = plan.Apply(encryptedSignature);
        if (signature.Length == 0)
        {
            return null;
        }

        var builder = new UriBuilder(mediaUrl);
        var existingQuery = builder.Query.TrimStart('?');
        var signatureComponent = Uri.EscapeDataString(signatureParameter) + "=" + Uri.EscapeDataString(signature);
        builder.Query = string.IsNullOrEmpty(existingQuery)
            ? signatureComponent
            : existingQuery + "&" + signatureComponent;
        return builder.Uri;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = Decode(equals < 0 ? pair : pair[..equals]);
            var value = Decode(equals < 0 ? string.Empty : pair[(equals + 1)..]);
            values.TryAdd(key, value);
        }

        return values;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace('+', ' '));
}
