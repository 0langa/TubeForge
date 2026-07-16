using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class HostRequestGateTests
{
    [Test]
    public static async Task GroupsServiceSubdomainsAndEnforcesPerHostConcurrency()
    {
        var gate = new HostRequestGate(maximumConcurrency: 1);
        var first = await gate.EnterAsync(new Uri("https://r1.googlevideo.com/video"));
        var blocked = gate.EnterAsync(new Uri("https://r2.googlevideo.com/audio")).AsTask();
        var independent = gate.EnterAsync(new Uri("https://example.com/file")).AsTask();

        Assert.False(blocked.IsCompleted);
        using var independentLease = await independent;
        first.Dispose();
        using var secondLease = await blocked;
        Assert.Equal("youtube.com", HostRequestGate.HostGroup(new Uri("https://music.youtube.com/watch")));
        Assert.Equal("ytimg.com", HostRequestGate.HostGroup(new Uri("https://i.ytimg.com/image")));
    }

    [Test]
    public static async Task AppliesSharedBoundedRateLimitDelay()
    {
        var now = new DateTimeOffset(2026, 7, 16, 20, 0, 0, TimeSpan.Zero);
        var delays = new List<TimeSpan>();
        var gate = new HostRequestGate(
            maximumConcurrency: 1,
            utcNow: () => now,
            delay: (delay, _) =>
            {
                delays.Add(delay);
                now += delay;
                return Task.CompletedTask;
            });
        var firstHost = new Uri("https://www.youtube.com/watch");
        gate.Defer(firstHost, TimeSpan.FromHours(1));

        using var lease = await gate.EnterAsync(new Uri("https://music.youtube.com/playlist"));

        Assert.Equal(1, delays.Count);
        Assert.Equal(HostRequestGate.MaximumRateLimitDelay, delays[0]);
    }

    [Test]
    public static void RejectsUnsafeLimitsAndRelativeUris()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new HostRequestGate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new HostRequestGate(5));
        Assert.Throws<ArgumentException>(() => HostRequestGate.HostGroup(new Uri("relative", UriKind.Relative)));
    }
}
