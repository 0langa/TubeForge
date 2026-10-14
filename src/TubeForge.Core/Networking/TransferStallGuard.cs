using TubeForge.Core.Errors;

namespace TubeForge.Core.Networking;

/// <summary>
/// Cancels a network transfer that stops delivering bytes. The media client deliberately has no
/// overall timeout because a large download can legitimately take hours, so without this a
/// connection that goes silent — a dropped Wi-Fi link, a NAT mapping that expired, a server that
/// stops sending — waits forever instead of failing into the retry and resume path. Callers pass
/// <see cref="Token"/> to every network wait and call <see cref="Progressed"/> whenever bytes arrive.
/// </summary>
public sealed class TransferStallGuard : IDisposable
{
    /// <summary>
    /// How long a request may wait for response headers or the next body bytes. Generous enough
    /// for throttled or slow links, which still deliver data, while bounding a dead connection.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public const string StalledCode = "Network.Stalled";

    private readonly CancellationTokenSource _source;
    private readonly CancellationToken _callerToken;
    private readonly TimeSpan _timeout;

    public TransferStallGuard(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        _timeout = timeout;
        _callerToken = cancellationToken;
        _source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _source.CancelAfter(timeout);
    }

    public CancellationToken Token => _source.Token;

    /// <summary>True when the guard, rather than the caller, cancelled the transfer.</summary>
    public bool HasStalled => _source.IsCancellationRequested && !_callerToken.IsCancellationRequested;

    /// <summary>Restarts the silence window after the transfer made progress.</summary>
    public void Progressed() => _source.CancelAfter(_timeout);

    /// <summary>
    /// Stops the silence window while the caller does local work between requests, however long
    /// that takes. <see cref="Progressed"/> starts it again. Restarting the window afterwards is
    /// not enough on its own: once the window has expired the token is already cancelled.
    /// </summary>
    public void Suspend() => _source.CancelAfter(Timeout.InfiniteTimeSpan);

    public static TimeSpan Validate(TimeSpan? timeout)
    {
        var value = timeout ?? DefaultTimeout;
        if (value <= TimeSpan.Zero || value == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout));
        }

        return value;
    }

    /// <summary>Transient error reported for a transfer the guard cancelled.</summary>
    public static TubeForgeError StalledError(TimeSpan timeout) => new(
        StalledCode,
        "The server stopped sending data.",
        FormattableString.Invariant($"No response bytes arrived for {timeout.TotalSeconds:0.#} seconds."),
        IsTransient: true);

    public void Dispose() => _source.Dispose();
}
