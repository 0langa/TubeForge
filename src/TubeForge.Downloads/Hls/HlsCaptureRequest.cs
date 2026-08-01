using TubeForge.Core.Media;

namespace TubeForge.Downloads.Hls;

public sealed record HlsCaptureRequest
{
    public required Uri ManifestUri { get; init; }

    public required string DestinationPath { get; init; }

    public required LiveCaptureOptions Options { get; init; }

    public string? HttpUserAgent { get; init; }
}
