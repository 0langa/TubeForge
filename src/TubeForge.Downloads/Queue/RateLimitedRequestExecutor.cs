using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Downloads.Queue;

public sealed class RateLimitedRequestExecutor(HostRequestGate hostGate)
{
    public const int MaximumAttempts = 3;

    private readonly HostRequestGate _hostGate =
        hostGate ?? throw new ArgumentNullException(nameof(hostGate));

    public async Task<Result<T>> ExecuteAsync<T>(
        Uri hostUri,
        Func<CancellationToken, Task<Result<T>>> operation,
        Action<TimeSpan>? retrying = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        try
        {
            for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
            {
                using var lease = await _hostGate.EnterAsync(hostUri, cancellationToken).ConfigureAwait(false);
                var result = await operation(cancellationToken).ConfigureAwait(false);
                if (result.IsSuccess || result.Error!.Code != "Network.RateLimited")
                {
                    return result;
                }

                var delay = EffectiveDelay(result.Error.RetryAfter);
                _hostGate.Defer(hostUri, delay);
                if (attempt == MaximumAttempts)
                {
                    return result;
                }

                retrying?.Invoke(delay);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Result<T>.Failure(new TubeForgeError(
                "Operation.Cancelled",
                "The request was cancelled."));
        }

        throw new InvalidOperationException("The bounded retry loop ended unexpectedly.");
    }

    internal static TimeSpan EffectiveDelay(TimeSpan? retryAfter)
    {
        var delay = retryAfter ?? HostRequestGate.DefaultRateLimitDelay;
        if (delay < TimeSpan.FromMilliseconds(250))
        {
            return TimeSpan.FromMilliseconds(250);
        }

        return delay > HostRequestGate.MaximumRateLimitDelay
            ? HostRequestGate.MaximumRateLimitDelay
            : delay;
    }
}
