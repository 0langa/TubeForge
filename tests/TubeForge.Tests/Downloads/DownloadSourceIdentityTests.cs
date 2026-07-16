using TubeForge.Core.YouTube;
using TubeForge.Downloads.Queue;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class DownloadSourceIdentityTests
{
    [Test]
    public static void RoundTripsDirectAndAdaptiveSelections()
    {
        Assert.True(YouTubeVideoId.TryCreate("dQw4w9WgXcQ", out var videoId));

        var directValue = DownloadSourceIdentity.Create(videoId, 18);
        var adaptiveValue = DownloadSourceIdentity.Create(videoId, 401, 140);

        Assert.True(DownloadSourceIdentity.TryParse(directValue, out var direct));
        Assert.Equal(18, direct.PrimaryFormatId);
        Assert.True(direct.AudioFormatId is null);
        Assert.True(DownloadSourceIdentity.TryParse(adaptiveValue, out var adaptive));
        Assert.Equal(401, adaptive.PrimaryFormatId);
        Assert.Equal(140, adaptive.AudioFormatId);
    }

    [Test]
    public static void RejectsMalformedAndAmbiguousIdentities()
    {
        foreach (var value in new[]
                 {
                     "", "bad:18", "dQw4w9WgXcQ", "dQw4w9WgXcQ:0", "dQw4w9WgXcQ:18+",
                     "dQw4w9WgXcQ:18+0", "dQw4w9WgXcQ:18+140+251", "dQw4w9WgXcQ:18:140"
                 })
        {
            Assert.False(DownloadSourceIdentity.TryParse(value, out _));
        }
    }
}
