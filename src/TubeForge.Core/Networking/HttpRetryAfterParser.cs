using System.Net.Http.Headers;

namespace TubeForge.Core.Networking;

public static class HttpRetryAfterParser
{
    public static readonly TimeSpan MaximumDelay = TimeSpan.FromMinutes(5);

    public static TimeSpan? Parse(HttpResponseHeaders headers, DateTimeOffset? nowUtc = null)
    {
        ArgumentNullException.ThrowIfNull(headers);
        var retryAfter = headers.RetryAfter;
        if (retryAfter is null)
        {
            return null;
        }

        TimeSpan? delay = retryAfter.Delta;
        if (delay is null && retryAfter.Date is { } date)
        {
            delay = date - (nowUtc ?? DateTimeOffset.UtcNow);
        }

        if (delay is null || delay <= TimeSpan.Zero)
        {
            return null;
        }

        return delay > MaximumDelay ? MaximumDelay : delay;
    }
}
