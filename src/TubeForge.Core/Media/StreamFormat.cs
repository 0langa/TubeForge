namespace TubeForge.Core.Media;

public sealed record StreamFormat
{
    public required int FormatId { get; init; }

    public required Uri Url { get; init; }

    public string? HttpUserAgent { get; init; }

    public required MediaContainer Container { get; init; }

    public required StreamKind Kind { get; init; }

    public VideoCodec VideoCodec { get; init; }

    public AudioCodec AudioCodec { get; init; }

    public int? Width { get; init; }

    public int? Height { get; init; }

    public int? FramesPerSecond { get; init; }

    public long? Bitrate { get; init; }

    public long? ContentLength { get; init; }

    public int? AudioSampleRate { get; init; }

    public bool IsHdr { get; init; }

    public string QualityLabel { get; init; } = string.Empty;

    public bool IsLiveHls { get; init; }

    public bool IsLiveManifestPending { get; init; }

    public bool HasVideo => Kind is StreamKind.Progressive or StreamKind.VideoOnly;

    public bool HasAudio => Kind is StreamKind.Progressive or StreamKind.AudioOnly;
}
