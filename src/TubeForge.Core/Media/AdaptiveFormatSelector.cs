namespace TubeForge.Core.Media;

public sealed record AudioVideoSelection(StreamFormat Video, StreamFormat? Audio)
{
    public bool RequiresMuxing => Audio is not null;

    public MediaContainer OutputContainer => Audio is null
        ? Video.Container
        : AdaptiveFormatSelector.ResolveOutputContainer(Video, Audio) ?? Video.Container;

    public long? ExpectedLength => Video.ContentLength is not null && Audio?.ContentLength is not null
        ? Video.ContentLength.Value + Audio.ContentLength.Value
        : RequiresMuxing ? null : Video.ContentLength;
}

public static class AdaptiveFormatSelector
{
    public static AudioVideoSelection? SelectBest(IEnumerable<StreamFormat> formats)
    {
        ArgumentNullException.ThrowIfNull(formats);
        var materialized = formats.ToArray();
        var audioFormats = materialized
            .Where(format => format.Kind == StreamKind.AudioOnly)
            .ToArray();

        foreach (var video in materialized
                     .Where(format => format.Kind == StreamKind.VideoOnly)
                     .OrderByDescending(format => format.Height ?? 0)
                     .ThenByDescending(format => format.FramesPerSecond ?? 0)
                     .ThenByDescending(format => format.IsHdr)
                     .ThenByDescending(format => format.Container == MediaContainer.Mp4)
                     .ThenByDescending(format => format.Bitrate ?? 0)
                     .ThenBy(format => format.FormatId))
        {
            var audio = SelectCompanionAudio(video, audioFormats);
            if (audio is not null)
            {
                return new AudioVideoSelection(video, audio);
            }
        }

        var progressive = FormatRanker.RecommendedProgressive(materialized);
        return progressive is null ? null : new AudioVideoSelection(progressive, null);
    }

    /// <summary>
    /// Selects the best audio track to combine with <paramref name="video"/>, preferring a
    /// native same-container companion (lossless MP4/WebM output) and falling back to the best
    /// cross-codec companion, which is muxed losslessly into Matroska.
    /// </summary>
    public static StreamFormat? SelectCompanionAudio(
        StreamFormat video,
        IEnumerable<StreamFormat> audioFormats)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audioFormats);
        var audios = audioFormats.Where(format => format.Kind == StreamKind.AudioOnly).ToArray();
        return BestAudio(audios.Where(audio => AreMuxCompatible(video, audio)))
            ?? BestAudio(audios.Where(audio => AreMkvMuxCompatible(video, audio)));
    }

    /// <summary>
    /// Resolves the lossless output container for a video/audio pair: the shared native container
    /// when the pair is natively muxable, otherwise Matroska. Returns null when the pair cannot be
    /// combined without re-encoding.
    /// </summary>
    public static MediaContainer? ResolveOutputContainer(StreamFormat video, StreamFormat audio)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);
        if (AreMuxCompatible(video, audio))
        {
            return video.Container;
        }

        return AreMkvMuxCompatible(video, audio) ? MediaContainer.Mkv : null;
    }

    public static bool AreMuxCompatible(StreamFormat video, StreamFormat audio)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);
        if (video.Kind != StreamKind.VideoOnly || audio.Kind != StreamKind.AudioOnly ||
            video.Container != audio.Container)
        {
            return false;
        }

        return video.Container switch
        {
            MediaContainer.Mp4 =>
                video.VideoCodec is VideoCodec.H264 or VideoCodec.Av1 &&
                audio.AudioCodec == AudioCodec.Aac,
            MediaContainer.WebM =>
                video.VideoCodec is VideoCodec.Vp9 or VideoCodec.Av1 &&
                audio.AudioCodec is AudioCodec.Opus or AudioCodec.Vorbis,
            _ => false
        };
    }

    /// <summary>
    /// Any decoded video codec combined with any decoded audio codec can be stream-copied into
    /// Matroska without re-encoding. Used as the universal cross-container fallback.
    /// </summary>
    public static bool AreMkvMuxCompatible(StreamFormat video, StreamFormat audio)
    {
        ArgumentNullException.ThrowIfNull(video);
        ArgumentNullException.ThrowIfNull(audio);
        return video.Kind == StreamKind.VideoOnly &&
            audio.Kind == StreamKind.AudioOnly &&
            video.VideoCodec is VideoCodec.H264 or VideoCodec.Vp9 or VideoCodec.Av1 &&
            audio.AudioCodec is AudioCodec.Aac or AudioCodec.Opus or AudioCodec.Vorbis;
    }

    private static StreamFormat? BestAudio(IEnumerable<StreamFormat> audios) =>
        audios
            .OrderByDescending(format => format.Bitrate ?? 0)
            .ThenByDescending(format => format.AudioSampleRate ?? 0)
            .ThenByDescending(format => format.AudioCodec == AudioCodec.Opus)
            .ThenBy(format => format.FormatId)
            .FirstOrDefault();
}
