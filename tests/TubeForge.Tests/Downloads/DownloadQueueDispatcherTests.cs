using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class DownloadQueueDispatcherTests
{
    [Test]
    public static void EnforcesGlobalConcurrencyAndRejectsDuplicateStarts()
    {
        var dispatcher = new DownloadQueueDispatcher(2);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        Assert.True(dispatcher.TryStart(first));
        Assert.False(dispatcher.TryStart(first));
        Assert.True(dispatcher.TryStart(second));
        Assert.False(dispatcher.TryStart(third));
        Assert.Equal(2, dispatcher.ActiveCount);

        Assert.True(dispatcher.Complete(first));
        Assert.True(dispatcher.TryStart(third));
        Assert.Equal(2, dispatcher.ActiveCount);
    }

    [Test]
    public static void AppliesChangedLimitWithoutCancellingActiveItems()
    {
        var dispatcher = new DownloadQueueDispatcher(3);
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var third = Guid.NewGuid();

        Assert.True(dispatcher.TryStart(first));
        Assert.True(dispatcher.TryStart(second));
        dispatcher.MaximumConcurrency = 1;

        Assert.Equal(2, dispatcher.ActiveCount);
        Assert.False(dispatcher.TryStart(third));
        Assert.True(dispatcher.Complete(first));
        Assert.False(dispatcher.TryStart(third));
        Assert.True(dispatcher.Complete(second));
        Assert.True(dispatcher.TryStart(third));
    }

    [Test]
    public static void RejectsUnsafeConcurrencyValuesAndEmptyIds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new DownloadQueueDispatcher(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new DownloadQueueDispatcher(5));

        var dispatcher = new DownloadQueueDispatcher();
        Assert.Throws<ArgumentException>(() => dispatcher.TryStart(Guid.Empty));
    }

    [Test]
    public static async Task ConcurrentSoakNeverExceedsGlobalLimitOrLeaksSlots()
    {
        const int maximumConcurrency = 4;
        var dispatcher = new DownloadQueueDispatcher(maximumConcurrency);
        var workers = Enumerable.Range(0, 32)
            .Select(worker => Task.Run(() =>
            {
                for (var iteration = 0; iteration < 250; iteration++)
                {
                    var itemId = DeterministicId(worker, iteration);
                    var spin = new SpinWait();
                    while (!dispatcher.TryStart(itemId))
                    {
                        if (spin.Count > 10_000)
                        {
                            throw new InvalidOperationException("Dispatcher did not release a queue slot.");
                        }

                        spin.SpinOnce();
                    }

                    Assert.True(dispatcher.ActiveCount <= maximumConcurrency);
                    Assert.True(dispatcher.IsActive(itemId));
                    Assert.True(dispatcher.Complete(itemId));
                }
            }))
            .ToArray();

        await Task.WhenAll(workers);

        Assert.Equal(0, dispatcher.ActiveCount);
    }

    private static Guid DeterministicId(int worker, int iteration)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, worker + 1);
        BitConverter.TryWriteBytes(bytes[4..], iteration + 1);
        BitConverter.TryWriteBytes(bytes[8..], ((long)worker << 32) | (uint)iteration | 1L);
        return new Guid(bytes);
    }
}
