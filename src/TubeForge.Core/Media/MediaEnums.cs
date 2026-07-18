namespace TubeForge.Core.Media;

public enum MediaContainer
{
    Unknown,
    Mp4,
    WebM,
    ThreeGp,
    Mkv
}

public enum VideoCodec
{
    None,
    Unknown,
    H264,
    Vp9,
    Av1
}

public enum AudioCodec
{
    None,
    Unknown,
    Aac,
    Opus,
    Vorbis
}

public enum StreamKind
{
    Progressive,
    VideoOnly,
    AudioOnly
}
