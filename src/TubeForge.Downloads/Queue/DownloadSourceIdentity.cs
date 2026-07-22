using TubeForge.Core.Media;
using TubeForge.Core.YouTube;

namespace TubeForge.Downloads.Queue;

public readonly record struct DownloadSourceIdentity(
    YouTubeVideoId VideoId,
    int PrimaryFormatId,
    int? AudioFormatId,
    OutputProfile Output = default,
    CaptionEmbedSelection? Caption = null,
    bool EmbedChapters = false,
    bool SplitChapters = false,
    MediaTrimRange? Trim = null,
    SponsorBlockSelection? SponsorBlock = null)
{
    public static string Create(
        YouTubeVideoId videoId,
        int primaryFormatId,
        int? audioFormatId = null,
        OutputProfile output = default,
        CaptionEmbedSelection? caption = null,
        bool embedChapters = false,
        bool splitChapters = false,
        MediaTrimRange? trim = null,
        SponsorBlockSelection? sponsorBlock = null)
    {
        if (primaryFormatId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(primaryFormatId), "Format IDs must be positive.");
        }

        if (audioFormatId is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(audioFormatId), "Format IDs must be positive.");
        }

        if (!output.IsValid)
        {
            throw new ArgumentException("The output profile is invalid.", nameof(output));
        }

        if (caption is { IsValid: false })
        {
            throw new ArgumentException("The embedded caption selection is invalid.", nameof(caption));
        }

        if (trim is { IsValid: false })
        {
            throw new ArgumentException("The trim range is invalid.", nameof(trim));
        }

        if (sponsorBlock is { IsValid: false })
        {
            throw new ArgumentException("The SponsorBlock selection is invalid.", nameof(sponsorBlock));
        }

        var streams = audioFormatId is null
            ? $"{videoId.Value}:{primaryFormatId}"
            : $"{videoId.Value}:{primaryFormatId}+{audioFormatId.Value}";
        var media = output.Kind == OutputProfileKind.Native
            ? streams
            : $"{streams}@{output.Identity}";
        var captioned = caption is null ? media : $"{media}~{caption.Value.Identity}";
        var chapterMode = (embedChapters, splitChapters) switch
        {
            (true, true) => "chapters+split",
            (true, false) => "chapters",
            (false, true) => "split",
            _ => null
        };
        var chaptered = chapterMode is null ? captioned : $"{captioned}^{chapterMode}";
        var trimmed = trim is null ? chaptered : $"{chaptered}%{trim.Value.Identity}";
        return sponsorBlock is null ? trimmed : $"{trimmed}&{sponsorBlock.Value.Identity}";
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
        SponsorBlockSelection? sponsorBlock = null;
        var ampersand = formatPart.IndexOf('&');
        if (ampersand != formatPart.LastIndexOf('&'))
        {
            return false;
        }

        if (ampersand >= 0)
        {
            if (ampersand == formatPart.Length - 1 ||
                !SponsorBlockSelection.TryParseIdentity(
                    formatPart[(ampersand + 1)..],
                    out var parsedSponsorBlock))
            {
                return false;
            }

            sponsorBlock = parsedSponsorBlock;
            formatPart = formatPart[..ampersand];
        }

        MediaTrimRange? trim = null;
        var percent = formatPart.IndexOf('%');
        if (percent != formatPart.LastIndexOf('%'))
        {
            return false;
        }

        if (percent >= 0)
        {
            if (percent == formatPart.Length - 1 ||
                !MediaTrimRange.TryParseIdentity(formatPart[(percent + 1)..], out var parsedTrim))
            {
                return false;
            }

            trim = parsedTrim;
            formatPart = formatPart[..percent];
        }

        var embedChapters = false;
        var splitChapters = false;
        var caret = formatPart.IndexOf('^');
        if (caret != formatPart.LastIndexOf('^'))
        {
            return false;
        }

        if (caret >= 0)
        {
            var chapterMode = formatPart[(caret + 1)..];
            if (chapterMode is not ("chapters" or "split" or "chapters+split"))
            {
                return false;
            }

            embedChapters = chapterMode is "chapters" or "chapters+split";
            splitChapters = chapterMode is "split" or "chapters+split";
            formatPart = formatPart[..caret];
        }

        CaptionEmbedSelection? caption = null;
        var tilde = formatPart.IndexOf('~');
        if (tilde != formatPart.LastIndexOf('~'))
        {
            return false;
        }

        if (tilde >= 0)
        {
            if (tilde == formatPart.Length - 1 ||
                !CaptionEmbedSelection.TryParseIdentity(formatPart[(tilde + 1)..], out var parsedCaption))
            {
                return false;
            }

            caption = parsedCaption;
            formatPart = formatPart[..tilde];
        }

        var at = formatPart.IndexOf('@');
        if (at != formatPart.LastIndexOf('@'))
        {
            return false;
        }

        var output = OutputProfile.Native;
        if (at >= 0)
        {
            if (at == formatPart.Length - 1 ||
                !OutputProfile.TryParseIdentity(formatPart[(at + 1)..], out output) ||
                output.Kind == OutputProfileKind.Native)
            {
                return false;
            }

            formatPart = formatPart[..at];
        }

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

        identity = new DownloadSourceIdentity(
            videoId,
            primaryFormatId,
            audioFormatId,
            output,
            caption,
            embedChapters,
            splitChapters,
            trim,
            sponsorBlock);
        return true;
    }
}
