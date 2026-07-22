using TubeForge.Core.Media;
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
        var convertedProfiles = new[]
        {
            AudioOutputProfile.Mp3(320),
            AudioOutputProfile.Aac(256),
            AudioOutputProfile.Opus(160),
            AudioOutputProfile.Wav,
            AudioOutputProfile.Flac
        };

        Assert.True(DownloadSourceIdentity.TryParse(directValue, out var direct));
        Assert.Equal(18, direct.PrimaryFormatId);
        Assert.True(direct.AudioFormatId is null);
        Assert.True(DownloadSourceIdentity.TryParse(adaptiveValue, out var adaptive));
        Assert.Equal(401, adaptive.PrimaryFormatId);
        Assert.Equal(140, adaptive.AudioFormatId);
        foreach (var profile in convertedProfiles)
        {
            var value = DownloadSourceIdentity.Create(videoId, 140, output: profile);
            Assert.True(DownloadSourceIdentity.TryParse(value, out var converted));
            Assert.Equal(profile, converted.Output);
            Assert.Equal($"dQw4w9WgXcQ:140@{profile.Identity}", value);
        }
    }

    [Test]
    public static void RejectsMalformedAndAmbiguousIdentities()
    {
        foreach (var value in new[]
                 {
                     "", "bad:18", "dQw4w9WgXcQ", "dQw4w9WgXcQ:0", "dQw4w9WgXcQ:18+",
                     "dQw4w9WgXcQ:18+0", "dQw4w9WgXcQ:18+140+251", "dQw4w9WgXcQ:18:140",
                     "dQw4w9WgXcQ:140@", "dQw4w9WgXcQ:140@native", "dQw4w9WgXcQ:140@mp3-96",
                     "dQw4w9WgXcQ:140@mp3-320@mp3-192", "dQw4w9WgXcQ:140@aac-96",
                     "dQw4w9WgXcQ:140@opus-320", "dQw4w9WgXcQ:140@wav-0",
                     "dQw4w9WgXcQ:140@flac-0", "dQw4w9WgXcQ:140@unknown-192"
                 })
        {
            Assert.False(DownloadSourceIdentity.TryParse(value, out _));
        }
    }
}
