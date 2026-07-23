using System.Globalization;

namespace TubeForge.Core.Media;

public readonly record struct MediaTrimRange(TimeSpan Start, TimeSpan End)
{
    public static readonly TimeSpan MaximumEnd = TimeSpan.FromDays(7);

    public bool IsValid =>
        Start >= TimeSpan.Zero &&
        End > Start &&
        End <= MaximumEnd &&
        Start.Ticks % TimeSpan.TicksPerMillisecond == 0 &&
        End.Ticks % TimeSpan.TicksPerMillisecond == 0;

    public TimeSpan Duration => End - Start;

    public string Identity =>
        $"{(long)Start.TotalMilliseconds}-{(long)End.TotalMilliseconds}";

    public static bool TryCreate(TimeSpan start, TimeSpan end, out MediaTrimRange range)
    {
        range = new MediaTrimRange(start, end);
        return range.IsValid;
    }

    public static bool TryParseIdentity(string? value, out MediaTrimRange range)
    {
        range = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 32)
        {
            return false;
        }

        var separator = value.IndexOf('-');
        if (separator <= 0 || separator != value.LastIndexOf('-') || separator == value.Length - 1 ||
            !long.TryParse(value[..separator], NumberStyles.None, CultureInfo.InvariantCulture, out var start) ||
            !long.TryParse(value[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var end))
        {
            return false;
        }

        try
        {
            return TryCreate(TimeSpan.FromMilliseconds(start), TimeSpan.FromMilliseconds(end), out range);
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
