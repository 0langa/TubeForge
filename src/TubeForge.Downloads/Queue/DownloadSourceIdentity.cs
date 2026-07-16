using TubeForge.Core.YouTube;

namespace TubeForge.Downloads.Queue;

public readonly record struct DownloadSourceIdentity(
    YouTubeVideoId VideoId,
    int PrimaryFormatId,
    int? AudioFormatId)
{
    public static string Create(YouTubeVideoId videoId, int primaryFormatId, int? audioFormatId = null)
    {
        if (primaryFormatId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(primaryFormatId), "Format IDs must be positive.");
        }

        if (audioFormatId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audioFormatId), "Format IDs must be positive.");
        }

        return audioFormatId is null
            ? $"{videoId.Value}:{primaryFormatId}"
            : $"{videoId.Value}:{primaryFormatId}+{audioFormatId.Value}";
    }

    public static bool TryParse(string? value, out DownloadSourceIdentity identity)
    {
        identity = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var colon = value.IndexOf(':');
        if (colon <= 0 || colon != value.LastIndexOf(':') || colon == value.Length - 1 ||
            !YouTubeVideoId.TryCreate(value[..colon], out var videoId))
        {
            return false;
        }

        var formatPart = value[(colon + 1)..];
        var plus = formatPart.IndexOf('+');
        if (plus != formatPart.LastIndexOf('+'))
        {
            return false;
        }

        var primaryPart = plus < 0 ? formatPart : formatPart[..plus];
        if (!int.TryParse(primaryPart, out var primaryFormatId) || primaryFormatId <= 0)
        {
            return false;
        }

        int? audioFormatId = null;
        if (plus >= 0)
        {
            if (plus == formatPart.Length - 1 ||
                !int.TryParse(formatPart[(plus + 1)..], out var parsedAudioFormatId) ||
                parsedAudioFormatId <= 0)
            {
                return false;
            }

            audioFormatId = parsedAudioFormatId;
        }

        identity = new DownloadSourceIdentity(videoId, primaryFormatId, audioFormatId);
        return true;
    }
}
