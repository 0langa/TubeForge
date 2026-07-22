namespace TubeForge.Core.Media;

public enum AudioOutputKind
{
    Native,
    Mp3,
    Aac,
    Opus,
    Wav,
    Flac
}

public readonly record struct AudioOutputProfile(AudioOutputKind Kind, int BitrateKbps = 0)
{
    public static AudioOutputProfile Native => new(AudioOutputKind.Native);

    public static AudioOutputProfile Mp3(int bitrateKbps) => new(AudioOutputKind.Mp3, bitrateKbps);

    public static AudioOutputProfile Aac(int bitrateKbps) => new(AudioOutputKind.Aac, bitrateKbps);

    public static AudioOutputProfile Opus(int bitrateKbps) => new(AudioOutputKind.Opus, bitrateKbps);

    public static AudioOutputProfile Wav => new(AudioOutputKind.Wav);

    public static AudioOutputProfile Flac => new(AudioOutputKind.Flac);

    public bool RequiresTranscode => Kind != AudioOutputKind.Native;

    public string DisplayName => Kind switch
    {
        AudioOutputKind.Native => "Native",
        AudioOutputKind.Mp3 => "MP3",
        AudioOutputKind.Aac => "AAC/M4A",
        AudioOutputKind.Opus => "Opus/OGG",
        AudioOutputKind.Wav => "WAV",
        AudioOutputKind.Flac => "FLAC",
        _ => throw new InvalidOperationException("Unknown audio output kind.")
    };

    public string Identity => Kind switch
    {
        AudioOutputKind.Native => "native",
        AudioOutputKind.Mp3 => $"mp3-{BitrateKbps}",
        AudioOutputKind.Aac => $"aac-{BitrateKbps}",
        AudioOutputKind.Opus => $"opus-{BitrateKbps}",
        AudioOutputKind.Wav => "wav",
        AudioOutputKind.Flac => "flac",
        _ => throw new InvalidOperationException("Unknown audio output kind.")
    };

    public string Extension => Kind switch
    {
        AudioOutputKind.Native => string.Empty,
        AudioOutputKind.Mp3 => ".mp3",
        AudioOutputKind.Aac => ".m4a",
        AudioOutputKind.Opus => ".ogg",
        AudioOutputKind.Wav => ".wav",
        AudioOutputKind.Flac => ".flac",
        _ => throw new InvalidOperationException("Unknown audio output kind.")
    };

    public bool IsValid => Kind switch
    {
        AudioOutputKind.Native => BitrateKbps == 0,
        AudioOutputKind.Mp3 => BitrateKbps is 128 or 192 or 256 or 320,
        AudioOutputKind.Aac => BitrateKbps is 128 or 192 or 256 or 320,
        AudioOutputKind.Opus => BitrateKbps is 96 or 128 or 160 or 192 or 256,
        AudioOutputKind.Wav or AudioOutputKind.Flac => BitrateKbps == 0,
        _ => false
    };

    public static bool TryParseIdentity(string? value, out AudioOutputProfile profile)
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
}
