using System.Globalization;
using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Updates;

public static class GitHubReleasePolicy
{
    public const long MaximumInstallerBytes = 512L * 1024 * 1024;
    public const long MaximumChecksumsBytes = 1024 * 1024;
    private const int MaximumAssets = 64;
    private const string Owner = "0langa";
    private const string Repository = "TubeForge";

    public static Result<UpdateRelease?> ParseLatest(string json, Version currentVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentNullException.ThrowIfNull(currentVersion);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                MaxDepth = 16,
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                Boolean(root, "draft") ||
                Boolean(root, "prerelease"))
            {
                return Invalid();
            }

            var tag = String(root, "tag_name");
            if (!TryParseStableVersion(tag, out var version))
            {
                return Invalid();
            }

            if (version <= currentVersion)
            {
                return Result<UpdateRelease?>.Success(null);
            }

            if (!TryHttpsUri(String(root, "html_url"), out var releasePage) ||
                releasePage.Host != "github.com" ||
                !releasePage.AbsolutePath.Equals(
                    $"/{Owner}/{Repository}/releases/tag/v{version}",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Invalid();
            }

            if (!root.TryGetProperty("assets", out var assets) ||
                assets.ValueKind != JsonValueKind.Array ||
                assets.GetArrayLength() is 0 or > MaximumAssets)
            {
                return Invalid();
            }

            var setupName = $"TubeForge-{version}-win-x64-setup.exe";
            var setup = FindAsset(assets, tag!, setupName, 1024 * 1024, MaximumInstallerBytes);
            var checksums = FindAsset(assets, tag!, "SHA256SUMS.txt", 1, MaximumChecksumsBytes);
            if (setup is null || checksums is null)
            {
                return Invalid();
            }

            return Result<UpdateRelease?>.Success(new UpdateRelease(
                version,
                releasePage,
                setupName,
                setup.Value.Uri,
                setup.Value.Length,
                setup.Value.Sha256,
                checksums.Value.Uri,
                checksums.Value.Length,
                checksums.Value.Sha256));
        }
        catch (JsonException)
        {
            return Invalid();
        }
    }

    public static Result<string> ParseSetupChecksum(
        ReadOnlySpan<byte> checksumBytes,
        string setupAssetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupAssetName);
        if (checksumBytes.IsEmpty || checksumBytes.Length > MaximumChecksumsBytes)
        {
            return InvalidChecksum();
        }

        var text = System.Text.Encoding.UTF8.GetString(checksumBytes);
        string? match = null;
        foreach (var line in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || !parts[1].Equals(setupAssetName, StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsSha256(parts[0]) || match is not null)
            {
                return InvalidChecksum();
            }

            match = parts[0].ToLowerInvariant();
        }

        return match is null
            ? InvalidChecksum()
            : Result<string>.Success(match);
    }

    private static Asset? FindAsset(
        JsonElement assets,
        string tag,
        string expectedName,
        long minimum,
        long maximum)
    {
        Asset? found = null;
        foreach (var element in assets.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !string.Equals(String(element, "name"), expectedName, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null ||
                !element.TryGetProperty("size", out var sizeElement) ||
                !sizeElement.TryGetInt64(out var size) ||
                size < minimum || size > maximum ||
                !TryDigest(String(element, "digest"), out var digest) ||
                !TryHttpsUri(String(element, "browser_download_url"), out var uri) ||
                uri.Host != "github.com" ||
                !uri.AbsolutePath.Equals(
                    $"/{Owner}/{Repository}/releases/download/{tag}/{expectedName}",
                    StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            found = new Asset(uri, size, digest);
        }

        return found;
    }

    private static bool TryParseStableVersion(string? tag, out Version version)
    {
        version = new Version();
        if (tag is null || tag.Length is < 6 or > 32 || tag[0] != 'v')
        {
            return false;
        }

        var parts = tag.AsSpan(1).ToString().Split('.');
        if (parts.Length != 3 || parts.Any(part =>
                part.Length == 0 ||
                (part.Length > 1 && part[0] == '0') ||
                !int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ||
                value is < 0 or > 65_535))
        {
            return false;
        }

        version = new Version(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture));
        return true;
    }

    private static bool TryDigest(string? value, out string digest)
    {
        const string prefix = "sha256:";
        digest = string.Empty;
        if (value is null || !value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(value[prefix.Length..]))
        {
            return false;
        }

        digest = value[prefix.Length..].ToLowerInvariant();
        return true;
    }

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(character => char.IsAsciiHexDigit(character));

    private static bool TryHttpsUri(string? value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps;

    private static bool Boolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.True;

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static Result<UpdateRelease?> Invalid() =>
        Result<UpdateRelease?>.Failure(new TubeForgeError(
            "Update.InvalidRelease",
            "The update release metadata failed TubeForge validation."));

    private static Result<string> InvalidChecksum() =>
        Result<string>.Failure(new TubeForgeError(
            "Update.InvalidChecksums",
            "The update checksum file did not contain one unambiguous installer hash."));

    private readonly record struct Asset(Uri Uri, long Length, string Sha256);
}
