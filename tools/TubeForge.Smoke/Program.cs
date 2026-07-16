using System.Net;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.YouTube;

if (args.Length != 2 || !args[0].Equals("analyze", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/TubeForge.Smoke -- analyze <youtube-url>");
    return 2;
}

var parsed = YouTubeUrlParser.ParseVideoId(args[1]);
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
using var client = new HttpClient(handler)
{
    Timeout = Timeout.InfiniteTimeSpan
};
var resolver = new YouTubeMetadataResolver(client);
var result = await resolver.ResolveAsync(parsed.Value);
if (!result.IsSuccess)
{
    Console.Error.WriteLine($"{result.Error!.Code}: {result.Error.Message}");
    if (!string.IsNullOrWhiteSpace(result.Error.TechnicalDetail))
    {
        Console.Error.WriteLine(result.Error.TechnicalDetail);
    }

    return 1;
}

var data = result.Value;
var progressive = data.Metadata.Formats.Count(format => format.Kind == StreamKind.Progressive);
var audioOnly = data.Metadata.Formats.Count(format => format.Kind == StreamKind.AudioOnly);
var videoOnly = data.Metadata.Formats.Count(format => format.Kind == StreamKind.VideoOnly);

Console.WriteLine($"Video ID: {data.Metadata.Id}");
Console.WriteLine($"Title: {data.Metadata.Title}");
Console.WriteLine($"Channel: {data.Metadata.Channel}");
Console.WriteLine($"Duration: {data.Metadata.Duration}");
Console.WriteLine($"Direct formats: {data.Metadata.Formats.Count} ({progressive} progressive, {audioOnly} audio-only, {videoOnly} video-only)");
Console.WriteLine($"Ciphered formats: {data.CipheredFormatCount}");
Console.WriteLine($"Player script detected: {data.PlayerScriptUrl is not null}");
if (data.Diagnostics is not null)
{
    Console.WriteLine($"Extractor stage: {data.Diagnostics.Stage}");
    Console.WriteLine($"Transform plans/probes: {data.Diagnostics.TransformPlanCount}/{data.Diagnostics.ProbeAttemptCount}");
}
return data.Metadata.Formats.Count > 0 ? 0 : 1;
