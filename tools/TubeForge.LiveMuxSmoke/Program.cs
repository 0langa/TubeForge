using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.Downloads;
using TubeForge.Media.IsoBmff;
using TubeForge.YouTube;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/TubeForge.LiveMuxSmoke -- <youtube-url>");
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
var resolver = new YouTubeMetadataResolver(client);
var resolved = await resolver.ResolveAsync(parsed.Value);
if (!resolved.IsSuccess)
{
    Console.Error.WriteLine($"{resolved.Error!.Code}: {resolved.Error.Message}");
    return 1;
}

var formats = resolved.Value.Metadata.Formats;
var audioFormats = formats
    .Where(format => format.Kind == StreamKind.AudioOnly && format.Container == MediaContainer.Mp4)
    .OrderBy(format => format.ContentLength ?? long.MaxValue)
    .ThenBy(format => format.FormatId)
    .ToArray();
var video = formats
    .Where(format => format.Kind == StreamKind.VideoOnly && format.Container == MediaContainer.Mp4)
    .Where(format => format.VideoCodec == VideoCodec.H264)
    .Where(format => audioFormats.Any(audio => AdaptiveFormatSelector.AreMuxCompatible(format, audio)))
    .OrderBy(format => format.ContentLength ?? long.MaxValue)
    .ThenBy(format => format.Height ?? int.MaxValue)
    .ThenBy(format => format.FormatId)
    .FirstOrDefault();
var audio = video is null
    ? null
    : audioFormats.FirstOrDefault(candidate => AdaptiveFormatSelector.AreMuxCompatible(video, candidate));
if (video is null || audio is null)
{
    Console.Error.WriteLine("No compatible adaptive MP4 pair was resolved.");
    return 1;
}

var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TubeForge.LiveMuxSmoke"));
var workDirectory = Path.Combine(safeRoot, Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(workDirectory);
try
{
    var output = Path.Combine(workDirectory, "verified.mp4");
    var direct = new DirectDownloadEngine(client);
    var adaptive = new AdaptiveDownloadEngine(direct);
    var result = await adaptive.DownloadAsync(new AdaptiveDownloadRequest
    {
        Video = Request(video, Path.Combine(workDirectory, "video-track.mp4")),
        Audio = Request(audio, Path.Combine(workDirectory, "audio-track.m4a")),
        DestinationPath = output,
        OutputContainer = MediaContainer.Mp4
    });
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"{result.Error!.Code}: {result.Error.Message}");
        return 1;
    }

    var structure = IsoBmffReader.ReadTopLevel(output);
    if (!structure.IsSuccess || structure.Value.Count(box => box.Type == "moof") == 0)
    {
        Console.Error.WriteLine("Live mux output did not retain a valid fragmented MP4 layout.");
        return 1;
    }

    var playback = await VerifyPlaybackAsync(output);
    if (!playback.IsSuccess)
    {
        Console.Error.WriteLine(playback.Error);
        return 1;
    }

    Console.WriteLine(
        $"Verified live MP4 mux: {video.Height}p video format {video.FormatId} + audio format {audio.FormatId}; " +
        $"{structure.Value.Count(box => box.Type == "moof")} fragments; {result.Value.BytesWritten} bytes; " +
        "Windows media stack opened video + audio.");
    return 0;
}
finally
{
    var resolvedWorkDirectory = Path.GetFullPath(workDirectory);
    if (!resolvedWorkDirectory.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException("Refusing to clean a live-mux directory outside the safe root.");
    }

    if (Directory.Exists(resolvedWorkDirectory))
    {
        Directory.Delete(resolvedWorkDirectory, recursive: true);
    }
}

DownloadRequest Request(StreamFormat format, string destination) => new()
{
    SourceUrl = format.Url,
    SourceIdentity = $"{resolved.Value.Metadata.Id.Value}:{format.FormatId}",
    DestinationPath = destination,
    ExpectedLength = format.ContentLength,
    ExpectedContainer = format.Container
};

static async Task<(bool IsSuccess, string? Error)> VerifyPlaybackAsync(string path)
{
    var completion = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
    var thread = new Thread(() =>
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var player = new MediaPlayer();
        var timer = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Normal, (_, _) =>
        {
            completion.TrySetResult((false, "Windows media stack timed out while opening mux output."));
            player.Close();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        }, dispatcher);
        player.MediaOpened += (_, _) =>
        {
            completion.TrySetResult(player.HasVideo && player.HasAudio
                ? (true, null)
                : (false, "Windows media stack did not detect both video and audio tracks."));
            timer.Stop();
            player.Close();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        };
        player.MediaFailed += (_, eventArgs) =>
        {
            completion.TrySetResult((false, $"Windows media stack rejected mux output: {eventArgs.ErrorException.GetType().Name}"));
            timer.Stop();
            player.Close();
            dispatcher.BeginInvokeShutdown(DispatcherPriority.Normal);
        };
        timer.Start();
        player.Open(new Uri(Path.GetFullPath(path)));
        Dispatcher.Run();
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.IsBackground = true;
    thread.Start();
    return await completion.Task.ConfigureAwait(false);
}
