namespace TubeForge.Core.Media;

public enum AudioOutputKind
{
    Native,
    Mp3
}

public readonly record struct AudioOutputProfile(AudioOutputKind Kind, int BitrateKbps = 0)
{
    public static AudioOutputProfile Native => new(AudioOutputKind.Native);

    public static AudioOutputProfile Mp3(int bitrateKbps) => new(AudioOutputKind.Mp3, bitrateKbps);

    public string Identity => Kind switch
    {
        AudioOutputKind.Native => "native",
        AudioOutputKind.Mp3 => $"mp3-{BitrateKbps}",
        _ => throw new InvalidOperationException("Unknown audio output kind.")
    };

    public string Extension => Kind switch
    {
        AudioOutputKind.Native => string.Empty,
        AudioOutputKind.Mp3 => ".mp3",
        _ => throw new InvalidOperationException("Unknown audio output kind.")
    };

    public bool IsValid => Kind switch
    {
        AudioOutputKind.Native => BitrateKbps == 0,
        AudioOutputKind.Mp3 => BitrateKbps is 128 or 192 or 256 or 320,
        _ => false
    };

    public static bool TryParseIdentity(string? value, out AudioOutputProfile profile)
    {
        profile = Native;
        if (string.Equals(value, "native", StringComparison.Ordinal))
        {
            return true;
        }

        const string prefix = "mp3-";
        if (value is null || !value.StartsWith(prefix, StringComparison.Ordinal) ||
            !int.TryParse(value.AsSpan(prefix.Length), out var bitrate))
        {
            return false;
        }

        profile = Mp3(bitrate);
        return profile.IsValid;
    }
}
