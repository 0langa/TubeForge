using System.Collections.Concurrent;

namespace TubeForge.Downloads.Queue;

public sealed class HostRequestGate
{
    public const int MaximumAllowedConcurrency = 4;
    public static readonly TimeSpan DefaultRateLimitDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MaximumRateLimitDelay = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, HostState> _hosts =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maximumConcurrency;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public HostRequestGate(int maximumConcurrency = 2)
        : this(maximumConcurrency, () => DateTimeOffset.UtcNow, Task.Delay)
    {
    }

    internal HostRequestGate(
        int maximumConcurrency,
        Func<DateTimeOffset> utcNow,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        if (maximumConcurrency is < 1 or > MaximumAllowedConcurrency)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        }

        _maximumConcurrency = maximumConcurrency;
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
    }

    public async ValueTask<IDisposable> EnterAsync(Uri uri, CancellationToken cancellationToken = default)
    {
        var state = StateFor(uri);
        while (true)
        {
            await state.Concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            var wait = state.DelayFrom(_utcNow());
            if (wait <= TimeSpan.Zero)
            {
                return new Lease(state.Concurrency);
            }

            state.Concurrency.Release();
            await _delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Defer(Uri uri, TimeSpan? retryAfter = null)
    {
        var delay = retryAfter ?? DefaultRateLimitDelay;
        if (delay < TimeSpan.FromMilliseconds(250))
        {
            delay = TimeSpan.FromMilliseconds(250);
        }
        else if (delay > MaximumRateLimitDelay)
        {
            delay = MaximumRateLimitDelay;
        }

        StateFor(uri).DeferUntil(_utcNow() + delay);
    }

    internal static string HostGroup(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            throw new ArgumentException("Host request URI must be absolute.", nameof(uri));
        }

        foreach (var serviceHost in new[] { "googlevideo.com", "youtube.com", "ytimg.com" })
        {
            if (uri.IdnHost.Equals(serviceHost, StringComparison.OrdinalIgnoreCase) ||
                uri.IdnHost.EndsWith('.' + serviceHost, StringComparison.OrdinalIgnoreCase))
            {
                return serviceHost;
            }
        }

        return uri.IdnHost.ToLowerInvariant();
    }

    private HostState StateFor(Uri uri) =>
        _hosts.GetOrAdd(HostGroup(uri), _ => new HostState(_maximumConcurrency));

    private sealed class HostState(int maximumConcurrency)
    {
        private readonly object _sync = new();
        private DateTimeOffset _notBeforeUtc = DateTimeOffset.MinValue;

        public SemaphoreSlim Concurrency { get; } = new(maximumConcurrency, maximumConcurrency);

        public TimeSpan DelayFrom(DateTimeOffset nowUtc)
        {
            lock (_sync)
            {
                return _notBeforeUtc - nowUtc;
            }
        }

        public void DeferUntil(DateTimeOffset value)
        {
            lock (_sync)
            {
                if (value > _notBeforeUtc)
                {
                    _notBeforeUtc = value;
                }
            }
        }
    }

    private sealed class Lease(SemaphoreSlim semaphore) : IDisposable
    {
        private SemaphoreSlim? _semaphore = semaphore;

        public void Dispose() => Interlocked.Exchange(ref _semaphore, null)?.Release();
    }
}
