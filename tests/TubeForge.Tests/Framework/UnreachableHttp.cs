namespace TubeForge.Tests.Framework;

/// <summary>
/// A real <see cref="SocketsHttpHandler"/> whose connection attempt never completes, so its
/// <see cref="SocketsHttpHandler.ConnectTimeout"/> fires. HttpClient reports that as a
/// TaskCanceledException although the caller cancelled nothing, which is what a dead network
/// (DNS or TCP connect never answering) looks like to the download engines. Nothing leaves the
/// machine: the connect callback replaces DNS and the socket entirely.
/// </summary>
internal static class UnreachableHttp
{
    public static SocketsHttpHandler CreateHandler(Action? connecting = null) => new()
    {
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromMilliseconds(200),
        ConnectCallback = async (_, cancellationToken) =>
        {
            connecting?.Invoke();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            throw new InvalidOperationException("The fixture connection never completes.");
        }
    };
}
