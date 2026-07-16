using System.Globalization;

namespace TubeForge.Core.Media;

public static class FormatDisplay
{
    public static string Label(StreamFormat format)
    {
        var parts = new List<string>();

        if (format.HasVideo)
        {
            parts.Add(format.Height is > 0 ? $"{format.Height}p" : "Video");
            if (format.FramesPerSecond is > 30)
            {
                parts.Add($"{format.FramesPerSecond} FPS");
            }

            if (format.IsHdr)
            {
                parts.Add("HDR");
            }
        }
        else
        {
            parts.Add("Audio");
        }

        parts.Add(ContainerName(format.Container));
        parts.Add(format.Kind switch
        {
            StreamKind.Progressive => "audio + video",
            StreamKind.VideoOnly => "video only",
            StreamKind.AudioOnly => "audio only",
            _ => "unknown"
        });

        if (format.ContentLength is > 0)
        {
            parts.Add(ByteSize(format.ContentLength.Value));
        }

        return string.Join(" · ", parts);
    }

    public static string Extension(MediaContainer container) => container switch
    {
        MediaContainer.Mp4 => ".mp4",
        MediaContainer.WebM => ".webm",
        MediaContainer.ThreeGp => ".3gp",
        _ => ".bin"
    };

    public static string OutputExtension(StreamFormat format) =>
        format.Kind == StreamKind.AudioOnly && format.Container == MediaContainer.Mp4
            ? ".m4a"
            : Extension(format.Container);

    private static string ContainerName(MediaContainer container) => container switch
    {
        MediaContainer.Mp4 => "MP4",
        MediaContainer.WebM => "WebM",
        MediaContainer.ThreeGp => "3GP",
        _ => "Unknown"
    };

    private static string ByteSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var suffixIndex = 0;
        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        var format = value >= 100 || suffixIndex == 0 ? "0" : value >= 10 ? "0.0" : "0.00";
        return value.ToString(format, CultureInfo.InvariantCulture) + " " + suffixes[suffixIndex];
    }
}
