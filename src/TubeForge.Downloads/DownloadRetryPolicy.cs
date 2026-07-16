namespace TubeForge.Downloads;

public static class DownloadRetryPolicy
{
    public const int MaximumAttempts = 3;

    public static TimeSpan DelayBeforeAttempt(int completedAttempts)
    {
        if (completedAttempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(completedAttempts));
        }

        var exponentialMilliseconds = Math.Min(8_000, 500 * (1 << Math.Min(completedAttempts - 1, 4)));
        var jitterMilliseconds = Random.Shared.Next(0, 251);
        return TimeSpan.FromMilliseconds(exponentialMilliseconds + jitterMilliseconds);
    }
}
