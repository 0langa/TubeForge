using System.Net;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.Downloads;
using TubeForge.Transcoding;
using TubeForge.YouTube;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/TubeForge.TranscodeSmoke -- <youtube-url>");
    return 2;
}

var parsed = YouTubeUrlParser.ParseVideoId(args[0]);
if (!parsed.IsSuccess)
{
    Console.Error.WriteLine($"{parsed.Error!.Code}: {parsed.Error.Message}");
    return 2;
}

using var handler = new SocketsHttpHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    AllowAutoRedirect = true,
    MaxAutomaticRedirections = 5,
    ConnectTimeout = TimeSpan.FromSeconds(10),
    PooledConnectionLifetime = TimeSpan.FromMinutes(5)
};
using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
var resolved = await new YouTubeMetadataResolver(client).ResolveAsync(parsed.Value);
if (!resolved.IsSuccess)
{
    Console.Error.WriteLine($"{resolved.Error!.Code}: {resolved.Error.Message}");
    return 1;
}

var formats = resolved.Value.Metadata.Formats
    .Where(format => format.Kind == StreamKind.AudioOnly)
    .GroupBy(format => format.Container)
    .Where(group => group.Key is MediaContainer.Mp4 or MediaContainer.WebM)
    .Select(group => group
        .OrderByDescending(format => format.Bitrate ?? 0)
        .ThenBy(format => format.ContentLength ?? long.MaxValue)
        .ThenBy(format => format.FormatId)
        .First())
    .OrderBy(format => format.Container)
    .ToArray();
if (formats.Length == 0)
{
    Console.Error.WriteLine("No supported audio-only stream was resolved.");
    return 1;
}

var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TubeForge.TranscodeSmoke"));
var workDirectory = Path.Combine(safeRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workDirectory);
try
{
    var downloader = new DirectDownloadEngine(client);
    var transcoder = new WindowsMediaFoundationTranscoder();
    foreach (var format in formats)
    {
        var source = Path.Combine(workDirectory, $"source-{format.FormatId}{FormatDisplay.OutputExtension(format)}");
        var output = Path.Combine(workDirectory, $"output-{format.FormatId}.mp3");
        var download = await downloader.DownloadAsync(new DownloadRequest
        {
            SourceUrl = format.Url,
            SourceIdentity = $"{resolved.Value.Metadata.Id.Value}:{format.FormatId}",
            DestinationPath = source,
            ExpectedLength = format.ContentLength,
            ExpectedContainer = format.Container
        });
        if (!download.IsSuccess)
        {
            Console.Error.WriteLine($"{download.Error!.Code}: {download.Error.Message}");
            return 1;
        }

        var transcode = await transcoder.TranscodeAsync(new AudioTranscodeRequest
        {
            SourcePath = source,
            DestinationPath = output,
            Output = AudioOutputProfile.Mp3(192)
        });
        if (!transcode.IsSuccess)
        {
            Console.Error.WriteLine(
                $"{format.Container}/{format.AudioCodec}: {transcode.Error!.Code}: " +
                $"{transcode.Error.Message} {transcode.Error.TechnicalDetail}");
            return 1;
        }

        Console.WriteLine(
            $"Verified {format.Container}/{format.AudioCodec} format {format.FormatId} -> " +
            $"MP3 {transcode.Value.BitrateKbps} kbps; {transcode.Value.BytesWritten} bytes.");
    }

    return 0;
}
finally
{
    var resolvedWorkDirectory = Path.GetFullPath(workDirectory);
    if (!resolvedWorkDirectory.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Refusing to clean a transcode directory outside the safe root.");
    }

    if (Directory.Exists(resolvedWorkDirectory))
    {
        Directory.Delete(resolvedWorkDirectory, recursive: true);
    }
}
