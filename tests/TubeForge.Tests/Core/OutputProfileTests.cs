using TubeForge.Core.Media;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class OutputProfileTests
{
    [Test]
    public static void VideoProfilesUseBoundedResolutionAwareBitrateLadder()
    {
        var heights = new int?[] { 360, 720, 1080, 1440, 2160, 4320, null };
        var compatible = new[] { 2_500, 5_000, 8_000, 14_000, 30_000, 45_000, 8_000 };
        var efficient = new[] { 1_800, 3_500, 5_500, 9_000, 18_000, 28_000, 5_500 };

        for (var index = 0; index < heights.Length; index++)
        {
            Assert.Equal(
                compatible[index],
                OutputProfile.H264AacMp4.ForVideoHeight(heights[index]).VideoBitrateKbps);
            Assert.Equal(
                efficient[index],
                OutputProfile.H265AacMp4.ForVideoHeight(heights[index]).VideoBitrateKbps);
            Assert.Equal(
                efficient[index],
                OutputProfile.Vp9OpusWebM.ForVideoHeight(heights[index]).VideoBitrateKbps);
        }
    }

    [Test]
    public static void EveryPublishedProfileIdentityRoundTrips()
    {
        var profiles = new[]
        {
            OutputProfile.Native,
            OutputProfile.Mp3(320),
            OutputProfile.Aac(256),
            OutputProfile.Opus(160),
            OutputProfile.Wav,
            OutputProfile.Flac,
            OutputProfile.H264AacMp4.ForVideoHeight(2160),
            OutputProfile.H265AacMp4.ForVideoHeight(1080),
            OutputProfile.Vp9OpusWebM.ForVideoHeight(4320)
        };

        foreach (var profile in profiles)
        {
            Assert.True(OutputProfile.TryParseIdentity(profile.Identity, out var parsed));
            Assert.Equal(profile, parsed);
        }
    }

    [Test]
    public static void TranscodeForecastsCoverEncodedOutputAndPcmWorstCase()
    {
        var duration = TimeSpan.FromMinutes(10);
        var h264 = OutputProfile.H264AacMp4.ForVideoHeight(1080);

        Assert.Equal(25_048_576L, OutputProfile.Mp3(320).EstimateTranscodedBytes(duration));
        Assert.Equal(116_248_576L, OutputProfile.Wav.EstimateTranscodedBytes(duration));
        Assert.Equal(615_448_576L, h264.EstimateTranscodedBytes(duration));
        Assert.True(OutputProfile.Native.EstimateTranscodedBytes(duration) is null);
        Assert.True(h264.EstimateTranscodedBytes(TimeSpan.FromSeconds(-1)) is null);
    }
}
