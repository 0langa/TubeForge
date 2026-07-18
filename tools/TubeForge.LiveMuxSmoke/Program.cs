using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.Downloads;
using TubeForge.Media;
using TubeForge.Media.IsoBmff;
using TubeForge.YouTube;
using TubeForge.YouTube.Extraction;

if (args.Length == 4 && args[0].Equals("--remux-file", StringComparison.OrdinalIgnoreCase))
{
    var result = await new FfmpegMediaProcessor(args[1]).RemuxMp4Async(args[2], args[3]);
    if (!result.IsSuccess)
    {
        Console.Error.WriteLine($"{result.Error!.Code}: {result.Error.Message}");
        return 1;
    }

    Console.WriteLine($"Normalized indexed MP4: {result.Value.BytesWritten} bytes.");
    return 0;
}

if (args.Length == 2 &&
    (args[0].Equals("--file", StringComparison.OrdinalIgnoreCase) ||
     args[0].Equals("--video-file", StringComparison.OrdinalIgnoreCase)))
{
    var requireAudio = args[0].Equals("--file", StringComparison.OrdinalIgnoreCase);
    var playback = await VerifyPlaybackAsync(args[1], requireAudio);
    if (!playback.IsSuccess)
    {
        Console.Error.WriteLine(playback.Error);
        return 1;
    }

    Console.WriteLine(
        $"Windows media stack opened '{Path.GetFileName(args[1])}' with video" +
        (requireAudio ? " + audio." : "."));
    return 0;
}

if (args.Length == 2 && args[0].Equals("--probe", StringComparison.OrdinalIgnoreCase))
{
    return await ProbeAsync(args[1]);
}

if (args.Length == 2 && args[0].Equals("--webm", StringComparison.OrdinalIgnoreCase))
{
    return await MuxWebMLiveAsync(args[1]);
}

if (args.Length == 2 && args[0].Equals("--mkv", StringComparison.OrdinalIgnoreCase))
{
    return await MuxMkvLiveAsync(args[1]);
}

if (args.Length != 1)
{
    Console.Error.WriteLine(
        "Usage: dotnet run --project tools/TubeForge.LiveMuxSmoke -- <youtube-url> | " +
        "--probe <youtube-url> | --webm <youtube-url> | --mkv <youtube-url> | " +
        "--file <mp4-path> | --video-file <mp4-path> | " +
        "--remux-file <ffmpeg-path> <input-path> <output-path>");
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
    var ffmpegPath = FindFfmpeg();
    if (ffmpegPath is null)
    {
        Console.Error.WriteLine("FFmpeg was not found in the bundled tool directory or PATH.");
        return 1;
    }

    var adaptive = new AdaptiveDownloadEngine(direct, new FfmpegMediaProcessor(ffmpegPath));
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
        if (!string.IsNullOrWhiteSpace(result.Error.TechnicalDetail))
        {
            Console.Error.WriteLine(result.Error.TechnicalDetail);
        }

        return 1;
    }

    var structure = IsoBmffReader.ReadTopLevel(output);
    if (!structure.IsSuccess ||
        structure.Value.Any(box => box.Type == "moof") ||
        structure.Value.Count(box => box.Type == "moov") != 1 ||
        structure.Value.Count(box => box.Type == "mdat") == 0)
    {
        Console.Error.WriteLine("Live mux output is not a conventional indexed MP4.");
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
        $"indexed stream-copy output; {result.Value.BytesWritten} bytes; " +
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
    HttpUserAgent = format.HttpUserAgent,
    SourceIdentity = $"{resolved.Value.Metadata.Id.Value}:{format.FormatId}",
    DestinationPath = destination,
    ExpectedLength = format.ContentLength,
    ExpectedContainer = format.Container
};

static async Task<(bool IsSuccess, string? Error)> VerifyPlaybackAsync(string path, bool requireAudio = true)
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
            completion.TrySetResult(player.HasVideo && (!requireAudio || player.HasAudio)
                ? (true, null)
                : (false, requireAudio
                    ? "Windows media stack did not detect both video and audio tracks."
                    : "Windows media stack did not detect a video track."));
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

static string? FindFfmpeg()
{
    var bundled = FfmpegMediaProcessor.BundledExecutablePath(AppContext.BaseDirectory);
    if (File.Exists(bundled))
    {
        return bundled;
    }

    var path = Environment.GetEnvironmentVariable("PATH");
    if (string.IsNullOrWhiteSpace(path))
    {
        return null;
    }

    return path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(directory => Path.Combine(directory, "ffmpeg.exe"))
        .FirstOrDefault(File.Exists);
}

static async Task<int> MuxMkvLiveAsync(string url)
{
    var parsedId = YouTubeUrlParser.ParseVideoId(url);
    if (!parsedId.IsSuccess)
    {
        Console.Error.WriteLine($"{parsedId.Error!.Code}: {parsedId.Error.Message}");
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
    var resolved = await resolver.ResolveAsync(parsedId.Value);
    if (!resolved.IsSuccess)
    {
        Console.Error.WriteLine($"{resolved.Error!.Code}: {resolved.Error.Message}");
        return 1;
    }

    var formats = resolved.Value.Metadata.Formats;
    // Deliberately cross-family: WebM VP9 video + MP4 AAC audio -> lossless MKV.
    var video = formats
        .Where(format => format.Kind == StreamKind.VideoOnly &&
            format.Container == MediaContainer.WebM && format.VideoCodec == VideoCodec.Vp9 &&
            (format.Height ?? 0) <= 480)
        .OrderByDescending(format => format.Height ?? 0)
        .FirstOrDefault();
    var audio = formats
        .Where(format => format.Kind == StreamKind.AudioOnly &&
            format.Container == MediaContainer.Mp4 && format.AudioCodec == AudioCodec.Aac)
        .OrderBy(format => format.Bitrate ?? long.MaxValue)
        .FirstOrDefault();
    if (video is null || audio is null)
    {
        Console.Error.WriteLine("No cross-container WebM-video + MP4-audio pair was resolved.");
        return 1;
    }

    var outputContainer = AdaptiveFormatSelector.ResolveOutputContainer(video, audio);
    if (outputContainer != MediaContainer.Mkv)
    {
        Console.Error.WriteLine($"Expected MKV cross-container resolution, got {outputContainer}.");
        return 1;
    }

    var ffmpegPath = FindFfmpeg();
    if (ffmpegPath is null)
    {
        Console.Error.WriteLine("FFmpeg was not found in the bundled tool directory or PATH.");
        return 1;
    }

    var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TubeForge.LiveMuxSmoke"));
    var workDirectory = Path.Combine(safeRoot, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workDirectory);
    try
    {
        var output = Path.Combine(workDirectory, "verified.mkv");
        var direct = new DirectDownloadEngine(client);
        var adaptive = new AdaptiveDownloadEngine(direct, new FfmpegMediaProcessor(ffmpegPath));
        var result = await adaptive.DownloadAsync(new AdaptiveDownloadRequest
        {
            Video = MkvRequest(resolved.Value, video, Path.Combine(workDirectory, "video-track.webm")),
            Audio = MkvRequest(resolved.Value, audio, Path.Combine(workDirectory, "audio-track.m4a")),
            DestinationPath = output,
            OutputContainer = MediaContainer.Mkv
        });
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"{result.Error!.Code}: {result.Error.Message}");
            if (!string.IsNullOrWhiteSpace(result.Error.TechnicalDetail))
            {
                Console.Error.WriteLine(result.Error.TechnicalDetail);
            }

            return 1;
        }

        var document = TubeForge.Media.Ebml.EbmlReader.ReadDocument(output);
        if (!document.IsSuccess)
        {
            Console.Error.WriteLine($"MKV mux output failed EBML structural validation: {document.Error!.Code}");
            return 1;
        }

        Console.WriteLine(
            $"Verified live MKV mux: {video.Height}p {video.VideoCodec} (WebM) format {video.FormatId} + " +
            $"{audio.AudioCodec} (MP4) format {audio.FormatId} -> Matroska stream-copy; " +
            $"{result.Value.BytesWritten} bytes; EBML structure valid.");
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

    static DownloadRequest MkvRequest(WatchPageData data, StreamFormat format, string destination) => new()
    {
        SourceUrl = format.Url,
        HttpUserAgent = format.HttpUserAgent,
        SourceIdentity = $"{data.Metadata.Id.Value}:{format.FormatId}",
        DestinationPath = destination,
        ExpectedLength = format.ContentLength,
        ExpectedContainer = format.Container
    };
}

static async Task<int> MuxWebMLiveAsync(string url)
{
    var parsedId = YouTubeUrlParser.ParseVideoId(url);
    if (!parsedId.IsSuccess)
    {
        Console.Error.WriteLine($"{parsedId.Error!.Code}: {parsedId.Error.Message}");
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
    var resolved = await resolver.ResolveAsync(parsedId.Value);
    if (!resolved.IsSuccess)
    {
        Console.Error.WriteLine($"{resolved.Error!.Code}: {resolved.Error.Message}");
        return 1;
    }

    var formats = resolved.Value.Metadata.Formats;
    var webmVideos = formats
        .Where(format => format.Kind == StreamKind.VideoOnly && format.Container == MediaContainer.WebM)
        .ToArray();
    var video = webmVideos
        .Where(format => (format.Height ?? 0) <= 480)
        .OrderByDescending(format => format.Height ?? 0)
        .ThenByDescending(format => format.Bitrate ?? 0)
        .FirstOrDefault()
        ?? webmVideos.OrderBy(format => format.Height ?? int.MaxValue).FirstOrDefault();
    var audio = video is null
        ? null
        : formats
            .Where(format => format.Kind == StreamKind.AudioOnly &&
                AdaptiveFormatSelector.AreMuxCompatible(video, format))
            .OrderByDescending(format => format.Bitrate ?? 0)
            .FirstOrDefault();
    if (video is null || audio is null)
    {
        Console.Error.WriteLine("No compatible adaptive WebM pair was resolved.");
        return 1;
    }

    var ffmpegPath = FindFfmpeg();
    if (ffmpegPath is null)
    {
        Console.Error.WriteLine("FFmpeg was not found in the bundled tool directory or PATH.");
        return 1;
    }

    var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TubeForge.LiveMuxSmoke"));
    var workDirectory = Path.Combine(safeRoot, Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(workDirectory);
    try
    {
        var output = Path.Combine(workDirectory, "verified.webm");
        var direct = new DirectDownloadEngine(client);
        var adaptive = new AdaptiveDownloadEngine(direct, new FfmpegMediaProcessor(ffmpegPath));
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await adaptive.DownloadAsync(new AdaptiveDownloadRequest
        {
            Video = LiveRequest(resolved.Value, video, Path.Combine(workDirectory, "video-track.webm")),
            Audio = LiveRequest(resolved.Value, audio, Path.Combine(workDirectory, "audio-track.webm")),
            DestinationPath = output,
            OutputContainer = MediaContainer.WebM
        });
        stopwatch.Stop();
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"{result.Error!.Code}: {result.Error.Message}");
            if (!string.IsNullOrWhiteSpace(result.Error.TechnicalDetail))
            {
                Console.Error.WriteLine(result.Error.TechnicalDetail);
            }

            return 1;
        }

        var document = TubeForge.Media.Ebml.EbmlReader.ReadDocument(output);
        if (!document.IsSuccess)
        {
            Console.Error.WriteLine($"WebM mux output failed EBML structural validation: {document.Error!.Code}");
            return 1;
        }

        var downloadedBytes = result.Value.VideoBytes + result.Value.AudioBytes;
        var seconds = Math.Max(stopwatch.Elapsed.TotalSeconds, 0.001);
        var throughput = downloadedBytes / 1_048_576.0 / seconds;
        var playback = await VerifyPlaybackAsync(output);
        Console.WriteLine(
            $"Verified live WebM mux: {video.Height}p {video.VideoCodec} format {video.FormatId} + " +
            $"{audio.AudioCodec} format {audio.FormatId}; ffmpeg stream-copy; " +
            $"{result.Value.BytesWritten} bytes; downloaded {downloadedBytes / 1_048_576.0:F1} MB " +
            $"in {seconds:F1}s ({throughput:F2} MB/s); EBML structure valid.");
        Console.WriteLine(playback.IsSuccess
            ? "Windows media stack opened WebM video + audio."
            : $"Playback check (codec-dependent, non-fatal): {playback.Error}");
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

    static DownloadRequest LiveRequest(WatchPageData data, StreamFormat format, string destination) => new()
    {
        SourceUrl = format.Url,
        HttpUserAgent = format.HttpUserAgent,
        SourceIdentity = $"{data.Metadata.Id.Value}:{format.FormatId}",
        DestinationPath = destination,
        ExpectedLength = format.ContentLength,
        ExpectedContainer = format.Container
    };
}

static async Task<int> ProbeAsync(string url)
{
    var parsedId = YouTubeUrlParser.ParseVideoId(url);
    if (!parsedId.IsSuccess)
    {
        Console.Error.WriteLine($"{parsedId.Error!.Code}: {parsedId.Error.Message}");
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
    var resolved = await resolver.ResolveAsync(parsedId.Value);
    if (!resolved.IsSuccess)
    {
        Console.Error.WriteLine($"{resolved.Error!.Code}: {resolved.Error.Message}");
        return 1;
    }

    var data = resolved.Value;
    var formats = data.Metadata.Formats;
    Console.WriteLine(
        $"stage={data.Diagnostics?.Stage ?? "(none)"} ciphered={data.CipheredFormatCount} " +
        $"formats={formats.Count} playerScript={(data.PlayerScriptUrl is null ? "no" : "yes")}");
    Console.WriteLine("kind        | res    | fps | hdr | vcodec | acodec | cont | kbps  | MB     | thr");
    foreach (var format in formats
                 .OrderBy(f => f.Kind switch
                 {
                     StreamKind.VideoOnly => 0,
                     StreamKind.Progressive => 1,
                     _ => 2
                 })
                 .ThenByDescending(f => f.Height ?? 0)
                 .ThenByDescending(f => f.Bitrate ?? 0))
    {
        var query = format.Url.Query;
        var throttled = query.Contains("&n=", StringComparison.Ordinal) ||
            query.Contains("?n=", StringComparison.Ordinal);
        Console.WriteLine(string.Join(" | ", new[]
        {
            format.Kind.ToString().PadRight(11),
            (format.Height is int h ? $"{h}p" : "-").PadRight(6),
            (format.FramesPerSecond?.ToString() ?? "-").PadRight(3),
            (format.IsHdr ? "Y" : "-").PadRight(3),
            format.VideoCodec.ToString().PadRight(6),
            format.AudioCodec.ToString().PadRight(6),
            format.Container.ToString().PadRight(4),
            (format.Bitrate is long b ? (b / 1000).ToString() : "-").PadRight(5),
            (format.ContentLength is long len ? (len / 1_048_576.0).ToString("F1") : "-").PadRight(6),
            throttled ? "Y" : "-"
        }));
    }

    return 0;
}
