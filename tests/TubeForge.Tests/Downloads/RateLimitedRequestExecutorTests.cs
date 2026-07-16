using TubeForge.Core.Errors;
using TubeForge.Core.Results;
using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class RateLimitedRequestExecutorTests
{
    [Test]
    public static async Task RetriesWithSharedDelayThenReturnsSuccess()
    {
        var now = new DateTimeOffset(2026, 7, 16, 20, 0, 0, TimeSpan.Zero);
        var delays = new List<TimeSpan>();
        var gate = Gate(() => now, delay =>
        {
            delays.Add(delay);
            now += delay;
        });
        var executor = new RateLimitedRequestExecutor(gate);
        var attempts = 0;
        var retryNotices = new List<TimeSpan>();

        var result = await executor.ExecuteAsync(
            new Uri("https://www.youtube.com/"),
            _ => Task.FromResult(++attempts < 3
                ? RateLimited<int>(TimeSpan.FromSeconds(7))
                : Result<int>.Success(42)),
            retryNotices.Add);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(42, result.Value);
        Assert.Equal(3, attempts);
        Assert.SequenceEqual(new[] { TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(7) }, delays);
        Assert.SequenceEqual(delays, retryNotices);
    }

    [Test]
    public static async Task StopsAfterBoundedAttemptsAndDoesNotRetryOtherFailures()
    {
        var now = new DateTimeOffset(2026, 7, 16, 20, 0, 0, TimeSpan.Zero);
        var gate = Gate(() => now, delay => now += delay);
        var executor = new RateLimitedRequestExecutor(gate);
        var rateLimitedAttempts = 0;
        var persistent = await executor.ExecuteAsync<int>(
            new Uri("https://www.youtube.com/"),
            _ =>
            {
                rateLimitedAttempts++;
                return Task.FromResult(RateLimited<int>(retryAfter: null));
            });

        var otherAttempts = 0;
        var forbidden = await executor.ExecuteAsync<int>(
            new Uri("https://example.com/"),
            _ =>
            {
                otherAttempts++;
                return Task.FromResult(Result<int>.Failure(new TubeForgeError(
                    "Network.Forbidden",
                    "Forbidden.")));
            });

        Assert.False(persistent.IsSuccess);
        Assert.Equal(RateLimitedRequestExecutor.MaximumAttempts, rateLimitedAttempts);
        Assert.False(forbidden.IsSuccess);
        Assert.Equal(1, otherAttempts);
    }

    [Test]
    public static async Task ReturnsTypedCancellationWhileWaitingForBackoff()
    {
        var gate = new HostRequestGate(maximumConcurrency: 1);
        var host = new Uri("https://www.youtube.com/");
        gate.Defer(host, TimeSpan.FromMinutes(1));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var executor = new RateLimitedRequestExecutor(gate);

        var result = await executor.ExecuteAsync(
            host,
            _ => Task.FromResult(Result<int>.Success(1)),
            cancellationToken: cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal("Operation.Cancelled", result.Error?.Code);
    }

    private static HostRequestGate Gate(Func<DateTimeOffset> now, Action<TimeSpan> waited) => new(
        maximumConcurrency: 1,
        utcNow: now,
        delay: (delay, _) =>
        {
            waited(delay);
            return Task.CompletedTask;
        });

    private static Result<T> RateLimited<T>(TimeSpan? retryAfter) =>
        Result<T>.Failure(new TubeForgeError(
            "Network.RateLimited",
            "Rate limited.",
            IsTransient: true,
            RetryAfter: retryAfter));
}
