using System.Buffers;
using System.Net;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.YouTube.Sidecars;

public sealed class ThumbnailDownloadEngine(HttpClient httpClient)
{
    private const int BufferSize = 32 * 1024;
    private const int MaximumRedirects = 3;
    private const long MaximumThumbnailBytes = 20 * 1024 * 1024;
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<Result<ThumbnailDownloadReceipt>> DownloadAsync(
        ThumbnailDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request);
        if (validation is not null)
        {
            return Result<ThumbnailDownloadReceipt>.Failure(validation);
        }

        string? partialPath = null;
        try
        {
            var destination = Path.GetFullPath(request.DestinationPath);
            partialPath = destination + ".part";
            if (File.Exists(destination))
            {
                return Failure("Thumbnail.DestinationExists", "A thumbnail file already exists at the destination.");
            }

            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Failure("Thumbnail.InvalidDestination", "The thumbnail destination is invalid.");
            }

            Directory.CreateDirectory(directory);
            using var response = await SendTrustedAsync(request.SourceUrl, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var transient = response.StatusCode == HttpStatusCode.TooManyRequests ||
                                response.StatusCode == HttpStatusCode.RequestTimeout ||
                                (int)response.StatusCode >= 500;
                return Failure(
                    response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "Network.RateLimited"
                        : "Thumbnail.HttpError",
                    $"The thumbnail server returned HTTP {(int)response.StatusCode}.",
                    isTransient: transient);
            }

            if (response.Content.Headers.ContentLength is > MaximumThumbnailBytes)
            {
                return Failure("Thumbnail.TooLarge", "The thumbnail exceeds the safe size limit.");
            }

            var declaredType = response.Content.Headers.ContentType?.MediaType;
            if (declaredType is not null && !declaredType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return Failure("Thumbnail.InvalidImage", "The thumbnail server returned a non-image response.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(
                partialPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            var header = new byte[12];
            var headerLength = 0;
            long written = 0;
            try
            {
                while (true)
                {
                    var count = await input.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)
                        .ConfigureAwait(false);
                    if (count == 0)
                    {
                        break;
                    }

                    if (written + count > MaximumThumbnailBytes)
                    {
                        return Failure("Thumbnail.TooLarge", "The thumbnail exceeds the safe size limit.");
                    }

                    if (headerLength < header.Length)
                    {
                        var headerCopy = Math.Min(header.Length - headerLength, count);
                        buffer.AsSpan(0, headerCopy).CopyTo(header.AsSpan(headerLength));
                        headerLength += headerCopy;
                    }

                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    written += count;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            var detectedType = DetectMediaType(header.AsSpan(0, headerLength));
            if (detectedType is null || !ExtensionMatches(destination, detectedType))
            {
                return Failure("Thumbnail.InvalidImage", "The thumbnail bytes do not match a supported image format.");
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            output.Flush(flushToDisk: true);
            output.Close();
            File.Move(partialPath, destination, overwrite: false);
            partialPath = null;
            return Result<ThumbnailDownloadReceipt>.Success(new ThumbnailDownloadReceipt(
                destination,
                written,
                detectedType));
        }
        catch (OperationCanceledException)
        {
            return Failure("Operation.Cancelled", "The thumbnail download was cancelled.");
        }
        catch (UnsafeThumbnailRedirectException)
        {
            return Failure("Thumbnail.UnsafeRedirect", "The thumbnail request redirected to an untrusted location.");
        }
        catch (HttpRequestException exception)
        {
            return Failure(
                "Network.RequestFailed",
                "The thumbnail connection failed.",
                exception.GetType().Name,
                isTransient: true);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Failure(
                "Thumbnail.InvalidDestination",
                "The thumbnail destination is invalid.",
                exception.GetType().Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "Thumbnail.WriteFailed",
                "TubeForge could not save the thumbnail.",
                exception.GetType().Name);
        }
        finally
        {
            TryDelete(partialPath);
        }
    }

    public static string FileExtensionFor(Uri sourceUrl)
    {
        ArgumentNullException.ThrowIfNull(sourceUrl);
        var extension = Path.GetExtension(sourceUrl.AbsolutePath).ToLowerInvariant();
        return extension switch
        {
            ".jpeg" => "jpg",
            ".jpg" or ".png" or ".webp" => extension[1..],
            _ => "jpg"
        };
    }

    private async Task<HttpResponseMessage> SendTrustedAsync(Uri sourceUrl, CancellationToken cancellationToken)
    {
        var current = sourceUrl;
        for (var redirect = 0; redirect <= MaximumRedirects; redirect++)
        {
            if (!IsAllowedThumbnailUrl(current))
            {
                throw new UnsafeThumbnailRedirectException();
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, current);
            message.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
            var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!IsRedirect(response.StatusCode))
            {
                var finalUri = response.RequestMessage?.RequestUri ?? current;
                if (!IsAllowedThumbnailUrl(finalUri))
                {
                    response.Dispose();
                    throw new UnsafeThumbnailRedirectException();
                }

                return response;
            }

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null || redirect == MaximumRedirects)
            {
                throw new UnsafeThumbnailRedirectException();
            }

            current = location.IsAbsoluteUri ? location : new Uri(current, location);
        }

        throw new UnsafeThumbnailRedirectException();
    }

    private static TubeForgeError? Validate(ThumbnailDownloadRequest request)
    {
        if (request.SourceUrl is null || !IsAllowedThumbnailUrl(request.SourceUrl))
        {
            return new TubeForgeError("Thumbnail.UnsafeSource", "The thumbnail URL is not an approved YouTube image host.");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return new TubeForgeError("Thumbnail.InvalidDestination", "Select a thumbnail destination.");
        }

        string extension;
        try
        {
            extension = Path.GetExtension(request.DestinationPath).ToLowerInvariant();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new TubeForgeError(
                "Thumbnail.InvalidDestination",
                "The thumbnail destination is invalid.",
                exception.GetType().Name);
        }

        if (extension is not ".jpg" and not ".jpeg" and not ".png" and not ".webp")
        {
            return new TubeForgeError("Thumbnail.InvalidDestination", "Use a JPG, PNG, or WebP thumbnail extension.");
        }

        return null;
    }

    private static bool IsAllowedThumbnailUrl(Uri uri) =>
        uri.IsAbsoluteUri &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("ytimg.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".ytimg.com", StringComparison.OrdinalIgnoreCase));

    private static bool IsRedirect(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.Moved or
        HttpStatusCode.Redirect or
        HttpStatusCode.RedirectMethod or
        HttpStatusCode.TemporaryRedirect or
        HttpStatusCode.PermanentRedirect;

    private static string? DetectMediaType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (header.Length >= 12 &&
            header[..4].SequenceEqual("RIFF"u8) &&
            header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private static bool ExtensionMatches(string destination, string mediaType)
    {
        var extension = Path.GetExtension(destination);
        return mediaType switch
        {
            "image/jpeg" => extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase),
            "image/png" => extension.Equals(".png", StringComparison.OrdinalIgnoreCase),
            "image/webp" => extension.Equals(".webp", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static Result<ThumbnailDownloadReceipt> Failure(
        string code,
        string message,
        string? detail = null,
        bool isTransient = false) =>
        Result<ThumbnailDownloadReceipt>.Failure(new TubeForgeError(code, message, detail, isTransient));

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class UnsafeThumbnailRedirectException : HttpRequestException;
}
