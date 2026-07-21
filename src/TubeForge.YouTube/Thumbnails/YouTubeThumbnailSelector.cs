using System.Text.Json;

namespace TubeForge.YouTube.Thumbnails;

internal static class YouTubeThumbnailSelector
{
    private const double TargetAspectRatio = 16d / 9d;
    private const double WidescreenTolerance = 0.05d;

    public static Uri? SelectBest(JsonElement candidates)
    {
        Uri? largest = null;
        long largestArea = -1;
        Uri? largestWidescreen = null;
        long largestWidescreenArea = -1;

        foreach (var candidate in candidates.EnumerateArray())
        {
            var rawUrl = candidate.TryGetProperty("url", out var urlElement) &&
                         urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString()
                : null;
            if (!TryCreateTrustedUri(rawUrl, out var uri))
            {
                continue;
            }

            var width = ReadPositiveDimension(candidate, "width");
            var height = ReadPositiveDimension(candidate, "height");
            var area = width is not null && height is not null
                ? (long)width.Value * height.Value
                : 0;

            if (area >= largestArea)
            {
                largest = uri;
                largestArea = area;
            }

            if (width is not null &&
                height is not null &&
                Math.Abs((double)width.Value / height.Value - TargetAspectRatio) <= WidescreenTolerance &&
                area >= largestWidescreenArea)
            {
                largestWidescreen = uri;
                largestWidescreenArea = area;
            }
        }

        return largestWidescreen ?? largest;
    }

    private static int? ReadPositiveDimension(JsonElement candidate, string propertyName) =>
        candidate.TryGetProperty(propertyName, out var value) &&
        value.TryGetInt32(out var dimension) &&
        dimension > 0
            ? dimension
            : null;

    private static bool TryCreateTrustedUri(string? rawUrl, out Uri uri)
    {
        if (Uri.TryCreate(rawUrl, UriKind.Absolute, out var parsed) &&
            parsed.Scheme == Uri.UriSchemeHttps &&
            string.IsNullOrEmpty(parsed.UserInfo) &&
            (parsed.Host.Equals("ytimg.com", StringComparison.OrdinalIgnoreCase) ||
             parsed.Host.EndsWith(".ytimg.com", StringComparison.OrdinalIgnoreCase)))
        {
            uri = parsed;
            return true;
        }

        uri = null!;
        return false;
    }
}
