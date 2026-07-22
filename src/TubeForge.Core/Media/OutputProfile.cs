namespace TubeForge.Core.Media;

public enum OutputProfileKind
{
    Native,
    Mp3,
    Aac,
    Opus,
    Wav,
    Flac,
    H264AacMp4,
    H265AacMp4,
    Vp9OpusWebM
}

public readonly record struct OutputProfile(
    OutputProfileKind Kind,
    int BitrateKbps = 0,
    int VideoBitrateKbps = 0)
{
    public static OutputProfile Native => new(OutputProfileKind.Native);

    public static OutputProfile Mp3(int bitrateKbps) => new(OutputProfileKind.Mp3, bitrateKbps);

    public static OutputProfile Aac(int bitrateKbps) => new(OutputProfileKind.Aac, bitrateKbps);

    public static OutputProfile Opus(int bitrateKbps) => new(OutputProfileKind.Opus, bitrateKbps);

    public static OutputProfile Wav => new(OutputProfileKind.Wav);

    public static OutputProfile Flac => new(OutputProfileKind.Flac);

    public static OutputProfile H264AacMp4 => new(OutputProfileKind.H264AacMp4, 192, 5_000);

    public static OutputProfile H265AacMp4 => new(OutputProfileKind.H265AacMp4, 192, 3_500);

    public static OutputProfile Vp9OpusWebM => new(OutputProfileKind.Vp9OpusWebM, 160, 3_500);

    public bool RequiresTranscode => Kind != OutputProfileKind.Native;

    public bool IsAudioTranscode => Kind is
        OutputProfileKind.Mp3 or
        OutputProfileKind.Aac or
        OutputProfileKind.Opus or
        OutputProfileKind.Wav or
        OutputProfileKind.Flac;

    public bool IsVideoTranscode => Kind is
        OutputProfileKind.H264AacMp4 or
        OutputProfileKind.H265AacMp4 or
        OutputProfileKind.Vp9OpusWebM;

    public OutputProfile ForVideoHeight(int? height)
    {
        if (!IsVideoTranscode)
        {
            return this;
        }

        var highCompatibility = Kind == OutputProfileKind.H264AacMp4;
        var bitrate = height switch
        {
            null => highCompatibility ? 8_000 : 5_500,
            <= 480 => highCompatibility ? 2_500 : 1_800,
            <= 720 => highCompatibility ? 5_000 : 3_500,
            <= 1_080 => highCompatibility ? 8_000 : 5_500,
            <= 1_440 => highCompatibility ? 14_000 : 9_000,
            <= 2_160 => highCompatibility ? 30_000 : 18_000,
            _ => highCompatibility ? 45_000 : 28_000
        };
        return this with { VideoBitrateKbps = bitrate };
    }

    public long? EstimateTranscodedBytes(TimeSpan? duration)
    {
        if (!IsValid || !RequiresTranscode || duration is null || duration.Value < TimeSpan.Zero)
        {
            return null;
        }

        try
        {
            var bytesPerSecond = IsVideoTranscode
                ? (VideoBitrateKbps + BitrateKbps) * 1000d / 8d
                : BitrateKbps > 0
                    ? BitrateKbps * 1000d / 8d
                    : 48_000d * 2d * 2d;
            return checked((long)Math.Ceiling(duration.Value.TotalSeconds * bytesPerSecond) + 1024 * 1024);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    public string DisplayName => Kind switch
    {
        OutputProfileKind.Native => "Native",
        OutputProfileKind.Mp3 => "MP3",
        OutputProfileKind.Aac => "AAC/M4A",
        OutputProfileKind.Opus => "Opus/OGG",
        OutputProfileKind.Wav => "WAV",
        OutputProfileKind.Flac => "FLAC",
        OutputProfileKind.H264AacMp4 => "H.264/AAC MP4",
        OutputProfileKind.H265AacMp4 => "H.265/AAC MP4",
        OutputProfileKind.Vp9OpusWebM => "VP9/Opus WebM",
        _ => throw new InvalidOperationException("Unknown output kind.")
    };

    public string Identity => Kind switch
    {
        OutputProfileKind.Native => "native",
        OutputProfileKind.Mp3 => $"mp3-{BitrateKbps}",
        OutputProfileKind.Aac => $"aac-{BitrateKbps}",
        OutputProfileKind.Opus => $"opus-{BitrateKbps}",
        OutputProfileKind.Wav => "wav",
        OutputProfileKind.Flac => "flac",
        OutputProfileKind.H264AacMp4 => $"h264-aac-mp4-{VideoBitrateKbps}",
        OutputProfileKind.H265AacMp4 => $"h265-aac-mp4-{VideoBitrateKbps}",
        OutputProfileKind.Vp9OpusWebM => $"vp9-opus-webm-{VideoBitrateKbps}",
        _ => throw new InvalidOperationException("Unknown output kind.")
    };

    public string Extension => Kind switch
    {
        OutputProfileKind.Native => string.Empty,
        OutputProfileKind.Mp3 => ".mp3",
        OutputProfileKind.Aac => ".m4a",
        OutputProfileKind.Opus => ".ogg",
        OutputProfileKind.Wav => ".wav",
        OutputProfileKind.Flac => ".flac",
        OutputProfileKind.H264AacMp4 or OutputProfileKind.H265AacMp4 => ".mp4",
        OutputProfileKind.Vp9OpusWebM => ".webm",
        _ => throw new InvalidOperationException("Unknown output kind.")
    };

    public bool IsValid => Kind switch
    {
        OutputProfileKind.Native => BitrateKbps == 0 && VideoBitrateKbps == 0,
        OutputProfileKind.Mp3 => BitrateKbps is 128 or 192 or 256 or 320 && VideoBitrateKbps == 0,
        OutputProfileKind.Aac => BitrateKbps is 128 or 192 or 256 or 320 && VideoBitrateKbps == 0,
        OutputProfileKind.Opus => BitrateKbps is 96 or 128 or 160 or 192 or 256 && VideoBitrateKbps == 0,
        OutputProfileKind.Wav or OutputProfileKind.Flac => BitrateKbps == 0 && VideoBitrateKbps == 0,
        OutputProfileKind.H264AacMp4 => BitrateKbps == 192 &&
                                               VideoBitrateKbps is 2_500 or 5_000 or 8_000 or 14_000 or 30_000 or 45_000,
        OutputProfileKind.H265AacMp4 => BitrateKbps == 192 &&
                                               VideoBitrateKbps is 1_800 or 3_500 or 5_500 or 9_000 or 18_000 or 28_000,
        OutputProfileKind.Vp9OpusWebM => BitrateKbps == 160 &&
                                               VideoBitrateKbps is 1_800 or 3_500 or 5_500 or 9_000 or 18_000 or 28_000,
        _ => false
    };

    public static bool TryParseIdentity(string? value, out OutputProfile profile)
    {
        profile = Native;
        if (string.Equals(value, "native", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(value, "wav", StringComparison.Ordinal))
        {
            profile = Wav;
            return true;
        }

        if (string.Equals(value, "flac", StringComparison.Ordinal))
        {
            profile = Flac;
            return true;
        }

        if (string.Equals(value, "h264-aac-mp4", StringComparison.Ordinal))
        {
            profile = H264AacMp4;
            return true;
        }

        if (string.Equals(value, "h265-aac-mp4", StringComparison.Ordinal))
        {
            profile = H265AacMp4;
            return true;
        }

        if (string.Equals(value, "vp9-opus-webm", StringComparison.Ordinal))
        {
            profile = Vp9OpusWebM;
            return true;
        }

        if (TryParseVideoIdentity(value, "h264-aac-mp4-", H264AacMp4, out profile) ||
            TryParseVideoIdentity(value, "h265-aac-mp4-", H265AacMp4, out profile) ||
            TryParseVideoIdentity(value, "vp9-opus-webm-", Vp9OpusWebM, out profile))
        {
            return true;
        }

        if (value is null)
        {
            return false;
        }

        var separator = value.IndexOf('-');
        if (separator <= 0 || separator == value.Length - 1 ||
            separator != value.LastIndexOf('-') ||
            !int.TryParse(value.AsSpan(separator + 1), out var bitrate))
        {
            return false;
        }

        switch (value[..separator])
        {
            case "mp3":
                profile = Mp3(bitrate);
                break;
            case "aac":
                profile = Aac(bitrate);
                break;
            case "opus":
                profile = Opus(bitrate);
                break;
            default:
                return false;
        }

        return profile.IsValid;
    }

    private static bool TryParseVideoIdentity(
        string? value,
        string prefix,
        OutputProfile template,
        out OutputProfile profile)
    {
        profile = Native;
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(value.AsSpan(prefix.Length), out var videoBitrateKbps))
        {
            return false;
        }

        profile = template with { VideoBitrateKbps = videoBitrateKbps };
        return profile.IsValid;
    }
}
