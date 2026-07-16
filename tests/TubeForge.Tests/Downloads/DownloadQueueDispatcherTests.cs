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
}
