namespace TubeForge.Core.Media;

public static class FormatRanker
{
    public static IReadOnlyList<StreamFormat> RankForDownload(IEnumerable<StreamFormat> formats) =>
        formats
            .OrderByDescending(UsabilityTier)
            .ThenByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.FramesPerSecond ?? 0)
            .ThenByDescending(format => format.Bitrate ?? 0)
            .ThenBy(format => format.FormatId)
            .ToArray();

    public static StreamFormat? RecommendedProgressive(IEnumerable<StreamFormat> formats) =>
        formats
            .Where(format => format.Kind == StreamKind.Progressive)
            .OrderByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.FramesPerSecond ?? 0)
            .ThenByDescending(format => format.Container == MediaContainer.Mp4)
            .ThenByDescending(format => format.Bitrate ?? 0)
            .FirstOrDefault();

    public static StreamFormat? RecommendedAudio(IEnumerable<StreamFormat> formats) =>
        formats
            .Where(format => format.Kind == StreamKind.AudioOnly)
            .OrderByDescending(format => format.AudioCodec == AudioCodec.Opus)
            .ThenByDescending(format => format.Bitrate ?? 0)
            .ThenBy(format => format.FormatId)
            .FirstOrDefault();

    private static int UsabilityTier(StreamFormat format) => format.Kind switch
    {
        StreamKind.Progressive => 3,
        StreamKind.AudioOnly => 2,
        StreamKind.VideoOnly => 1,
        _ => 0
    };
}
