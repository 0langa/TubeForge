using System.Buffers;
using System.Net;
using System.Text;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.YouTube.Captions;

public sealed class CaptionDownloadEngine(HttpClient httpClient)
{
    private const int BufferSize = 32 * 1024;
    private const long MaximumCaptionBytes = 10 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    public async Task<Result<CaptionDownloadReceipt>> DownloadAsync(
        CaptionDownloadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var validation = Validate(request);
        if (validation is not null)
        {
            return Result<CaptionDownloadReceipt>.Failure(validation);
        }

        string? partialPath = null;
        try
        {
            var destination = Path.GetFullPath(request.DestinationPath);
            partialPath = destination + ".part";
            if (File.Exists(destination))
            {
                return Failure("Caption.DestinationExists", "A caption file already exists at the destination.");
            }

            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Failure("Caption.InvalidDestination", "The caption destination is invalid.");
            }

            Directory.CreateDirectory(directory);
            using var message = new HttpRequestMessage(HttpMethod.Get, BuildWebVttUrl(request.SourceUrl));
            message.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36");
            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            var finalUri = response.RequestMessage?.RequestUri ?? message.RequestUri!;
            if (!IsAllowedCaptionUrl(finalUri))
            {
                return Failure("Caption.UnsafeRedirect", "The caption request redirected to an untrusted location.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var transient = response.StatusCode == HttpStatusCode.TooManyRequests ||
                                response.StatusCode == HttpStatusCode.RequestTimeout ||
                                (int)response.StatusCode >= 500;
                return Failure(
                    response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "Network.RateLimited"
                        : "Caption.HttpError",
                    $"The caption server returned HTTP {(int)response.StatusCode}.",
                    isTransient: transient);
            }

            if (response.Content.Headers.ContentLength is > MaximumCaptionBytes)
            {
                return Failure("Caption.TooLarge", "The caption response exceeds the safe size limit.");
            }

            var bytes = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            string source;
            try
            {
                source = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Failure("Caption.InvalidEncoding", "The caption response is not valid UTF-8.");
            }

            var converted = request.OutputFormat == CaptionOutputFormat.SubRip
                ? WebVttCaptionConverter.ConvertToSubRip(source)
                : WebVttCaptionConverter.Normalize(source);
            if (!converted.IsSuccess)
            {
                return Result<CaptionDownloadReceipt>.Failure(converted.Error!);
            }

            var cueCount = WebVttCaptionConverter.CountCues(source);
            if (!cueCount.IsSuccess)
            {
                return Result<CaptionDownloadReceipt>.Failure(cueCount.Error!);
            }

            var outputBytes = new UTF8Encoding(false).GetBytes(converted.Value);
            await using (var output = new FileStream(
                             partialPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await output.WriteAsync(outputBytes, cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            File.Move(partialPath, destination, overwrite: false);
            return Result<CaptionDownloadReceipt>.Success(new CaptionDownloadReceipt(
                destination,
                outputBytes.LongLength,
                cueCount.Value));
        }
        catch (OperationCanceledException)
        {
            TryDelete(partialPath);
            return Failure("Operation.Cancelled", "The caption download was cancelled.");
        }
        catch (CaptionTooLargeException)
        {
            TryDelete(partialPath);
            return Failure("Caption.TooLarge", "The caption response exceeds the safe size limit.");
        }
        catch (HttpRequestException exception)
        {
            TryDelete(partialPath);
            return Failure(
                "Network.RequestFailed",
                "The caption connection failed.",
                exception.GetType().Name,
                isTransient: true);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            TryDelete(partialPath);
            return Failure(
                "Caption.InvalidDestination",
                "The caption destination is invalid.",
                exception.GetType().Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(partialPath);
            return Failure(
                "Caption.WriteFailed",
                "TubeForge could not save the caption file.",
                exception.GetType().Name);
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
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

                if (output.Length + count > MaximumCaptionBytes)
                {
                    throw new CaptionTooLargeException();
                }

                output.Write(buffer, 0, count);
            }

            return output.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static TubeForgeError? Validate(CaptionDownloadRequest request)
    {
        if (request.SourceUrl is null || !IsAllowedCaptionUrl(request.SourceUrl))
        {
            return new TubeForgeError("Caption.UnsafeSource", "The caption URL is not an approved YouTube endpoint.");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return new TubeForgeError("Caption.InvalidDestination", "Select a caption destination.");
        }

        if (!Enum.IsDefined(request.OutputFormat))
        {
            return new TubeForgeError("Caption.InvalidFormat", "The caption output format is invalid.");
        }

        var expectedExtension = request.OutputFormat == CaptionOutputFormat.SubRip ? ".srt" : ".vtt";
        try
        {
            if (!Path.GetExtension(request.DestinationPath).Equals(expectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return new TubeForgeError("Caption.InvalidDestination", "The caption extension does not match its format.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return new TubeForgeError(
                "Caption.InvalidDestination",
                "The caption destination is invalid.",
                exception.GetType().Name);
        }

        return null;
    }

    private static Uri BuildWebVttUrl(Uri source)
    {
        var builder = new UriBuilder(source);
        var queryParts = builder.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part => !part.StartsWith("fmt=", StringComparison.OrdinalIgnoreCase))
            .Append("fmt=vtt");
        builder.Query = string.Join('&', queryParts);
        return builder.Uri;
    }

    private static bool IsAllowedCaptionUrl(Uri uri) =>
        uri.IsAbsoluteUri &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase)) &&
        uri.AbsolutePath.Equals("/api/timedtext", StringComparison.OrdinalIgnoreCase);

    private static Result<CaptionDownloadReceipt> Failure(
        string code,
        string message,
        string? detail = null,
        bool isTransient = false) =>
        Result<CaptionDownloadReceipt>.Failure(new TubeForgeError(code, message, detail, isTransient));

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

    private sealed class CaptionTooLargeException : Exception;
}
