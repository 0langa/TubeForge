using System.Globalization;

namespace TubeForge.Core.Media;

public readonly record struct LiveCaptureOptions(
    TimeSpan MaximumDuration,
    long MaximumBytes,
    TimeSpan MaximumWaitForStart)
{
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaximumAllowedDuration = TimeSpan.FromHours(24);
    public const long MinimumBytes = 16L * 1024 * 1024;
    public const long MaximumAllowedBytes = 100L * 1024 * 1024 * 1024;
    public static readonly TimeSpan MaximumAllowedWait = TimeSpan.FromHours(24);

    public bool IsValid =>
        MaximumDuration >= MinimumDuration && MaximumDuration <= MaximumAllowedDuration &&
        MaximumDuration.Ticks % TimeSpan.TicksPerSecond == 0 &&
        MaximumBytes is >= MinimumBytes and <= MaximumAllowedBytes &&
        MaximumWaitForStart >= TimeSpan.Zero && MaximumWaitForStart <= MaximumAllowedWait &&
        MaximumWaitForStart.Ticks % TimeSpan.TicksPerSecond == 0;

    public string Identity => string.Create(
        CultureInfo.InvariantCulture,
        $"{(long)MaximumDuration.TotalSeconds}-{MaximumBytes}-{(long)MaximumWaitForStart.TotalSeconds}");

    public static bool TryParseIdentity(string? value, out LiveCaptureOptions options)
    {
        options = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
        {
            return false;
        }

        var parts = value.Split('-');
        if (parts.Length != 3 ||
            !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var durationSeconds) ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var maximumBytes) ||
            !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var waitSeconds))
        {
            return false;
        }

        try
        {
            options = new LiveCaptureOptions(
                TimeSpan.FromSeconds(durationSeconds),
                maximumBytes,
                TimeSpan.FromSeconds(waitSeconds));
            return options.IsValid && options.Identity == value;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
