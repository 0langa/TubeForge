using TubeForge.Core.Media;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class FormatRankerTests
{
    [Test]
    public static void RecommendsHighestProgressiveFormat()
    {
        var formats = new[]
        {
            Format(18, StreamKind.Progressive, MediaContainer.Mp4, 360, 500_000),
            Format(22, StreamKind.Progressive, MediaContainer.Mp4, 720, 2_000_000),
            Format(137, StreamKind.VideoOnly, MediaContainer.Mp4, 1080, 4_000_000),
            Format(251, StreamKind.AudioOnly, MediaContainer.WebM, null, 160_000)
        };

        Assert.Equal(22, FormatRanker.RecommendedProgressive(formats)!.FormatId);
        Assert.SequenceEqual(new[] { 22, 18, 251, 137 },
            FormatRanker.RankForDownload(formats).Select(format => format.FormatId));
    }

    [Test]
    public static void BuildsTruthfulDisplayLabel()
    {
        var format = Format(22, StreamKind.Progressive, MediaContainer.Mp4, 720, 2_000_000) with
        {
            FramesPerSecond = 60,
            ContentLength = 128L * 1024 * 1024
        };

        Assert.Equal("720p · 60 FPS · MP4 · audio + video · 128 MB", FormatDisplay.Label(format));
    }

    [Test]
    public static void UsesNativeAudioExtensionWithoutClaimingMp3Conversion()
    {
        var audio = Format(140, StreamKind.AudioOnly, MediaContainer.Mp4, null, 128_000);

        Assert.Equal(".m4a", FormatDisplay.OutputExtension(audio));
        Assert.Equal(".mp4", FormatDisplay.OutputExtension(
            Format(22, StreamKind.Progressive, MediaContainer.Mp4, 720, 2_000_000)));
    }

    [Test]
    public static void FiltersVideoSelectionsByModeAndExactCharacteristics()
    {
        var formats = new[]
        {
            Format(22, StreamKind.Progressive, MediaContainer.Mp4, 720, 2_000_000),
            Format(137, StreamKind.VideoOnly, MediaContainer.Mp4, 1080, 4_000_000) with
            {
                FramesPerSecond = 30
            },
            Format(303, StreamKind.VideoOnly, MediaContainer.WebM, 1080, 5_000_000) with
            {
                FramesPerSecond = 60,
                VideoCodec = VideoCodec.Vp9,
                IsHdr = true
            },
            Format(251, StreamKind.AudioOnly, MediaContainer.WebM, null, 160_000)
        };

        var matches = FormatFilter.Apply(formats, new FormatSelectionCriteria
        {
            Mode = DownloadMode.VideoOnly,
            Height = 1080,
            Container = MediaContainer.WebM,
            VideoCodec = VideoCodec.Vp9,
            FramesPerSecond = 60,
            IsHdr = true
        });

        Assert.SequenceEqual(new[] { 303 }, matches.Select(format => format.FormatId));
    }

    [Test]
    public static void FiltersAudioSelectionsByNativeContainerCodecAndBitrate()
    {
        var formats = new[]
        {
            Format(140, StreamKind.AudioOnly, MediaContainer.Mp4, null, 128_000),
            Format(250, StreamKind.AudioOnly, MediaContainer.WebM, null, 64_000) with
            {
                AudioCodec = AudioCodec.Opus
            },
            Format(251, StreamKind.AudioOnly, MediaContainer.WebM, null, 160_000) with
            {
                AudioCodec = AudioCodec.Opus
            },
            Format(22, StreamKind.Progressive, MediaContainer.Mp4, 720, 2_000_000)
        };

        var matches = FormatFilter.Apply(formats, new FormatSelectionCriteria
        {
            Mode = DownloadMode.AudioOnly,
            Container = MediaContainer.WebM,
            AudioCodec = AudioCodec.Opus,
            Bitrate = 160_000
        });

        Assert.SequenceEqual(new[] { 251 }, matches.Select(format => format.FormatId));
    }

    private static StreamFormat Format(
        int id,
        StreamKind kind,
        MediaContainer container,
        int? height,
        long bitrate) => new()
        {
            FormatId = id,
            Url = new Uri($"https://example.test/{id}"),
            Kind = kind,
            Container = container,
            Height = height,
            Bitrate = bitrate,
            VideoCodec = kind == StreamKind.AudioOnly ? VideoCodec.None : VideoCodec.H264,
            AudioCodec = kind == StreamKind.VideoOnly ? AudioCodec.None : AudioCodec.Aac
        };
}
