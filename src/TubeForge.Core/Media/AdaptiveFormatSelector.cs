namespace TubeForge.Core.Media;

public sealed record AudioVideoSelection(StreamFormat Video, StreamFormat? Audio)
{
    public bool RequiresMuxing => Audio is not null;

    public MediaContainer OutputContainer => Video.Container;

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
            .OrderByDescending(format => format.Bitrate ?? 0)
            .ThenByDescending(format => format.AudioSampleRate ?? 0)
            .ThenByDescending(format => format.AudioCodec == AudioCodec.Opus)
            .ThenBy(format => format.FormatId)
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
            var audio = audioFormats.FirstOrDefault(candidate => AreMuxCompatible(video, candidate));
            if (audio is not null)
            {
                return new AudioVideoSelection(video, audio);
            }
        }

        var progressive = FormatRanker.RecommendedProgressive(materialized);
        return progressive is null ? null : new AudioVideoSelection(progressive, null);
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
}
