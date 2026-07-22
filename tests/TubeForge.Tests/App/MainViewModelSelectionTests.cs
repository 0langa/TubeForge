using System.Reflection;
using TubeForge.App.ViewModels;
using TubeForge.Core.Media;
using TubeForge.Core.Results;
using TubeForge.Core.YouTube;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.App;

public static class MainViewModelSelectionTests
{
    [Test]
    public static void EveryDependentFilterAndProcessingCombinationKeepsAValidExactOutput()
    {
        using var viewModel = new MainViewModel();
        SetFormats(viewModel, BuildCompleteCatalog());

        var terminalCombinations = 0;
        foreach (var mode in viewModel.DownloadModes.ToArray())
        {
            viewModel.SelectedDownloadMode = mode;
            switch (mode.Value)
            {
                case DownloadMode.AudioOnly:
                    ExerciseAudioOnly(viewModel, ref terminalCombinations);
                    break;
                case DownloadMode.AudioVideo:
                    ExerciseVideo(viewModel, includeAudioFilters: true, ref terminalCombinations);
                    break;
                case DownloadMode.VideoOnly:
                    ExerciseVideo(viewModel, includeAudioFilters: false, ref terminalCombinations);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected mode {mode.Value}.");
            }
        }

        Assert.True(terminalCombinations >= 500,
            $"Expected broad dependent-filter coverage; exercised {terminalCombinations} terminal combinations.");
    }

    [Test]
    public static void EveryDecodedVideoAudioCodecAndContainerPairHasTruthfulLosslessOutput()
    {
        var id = 1;
        foreach (var videoContainer in new[] { MediaContainer.Mp4, MediaContainer.WebM })
            foreach (var videoCodec in new[] { VideoCodec.H264, VideoCodec.Vp9, VideoCodec.Av1 })
                foreach (var audioContainer in new[] { MediaContainer.Mp4, MediaContainer.WebM })
                    foreach (var audioCodec in new[] { AudioCodec.Aac, AudioCodec.Opus, AudioCodec.Vorbis })
                    {
                        var video = Video(id++, videoContainer, videoCodec, 1080, 60, isHdr: false);
                        var audio = Audio(id++, audioContainer, audioCodec, 192_000, 48_000);
                        var native = AdaptiveFormatSelector.AreMuxCompatible(video, audio);
                        var output = AdaptiveFormatSelector.ResolveOutputContainer(video, audio);

                        Assert.Equal(native ? videoContainer : MediaContainer.Mkv, output);
                        var display = new FormatItemViewModel(video, audio);
                        Assert.Equal(output, display.OutputContainer);
                        Assert.True(display.TechnicalLabel.Contains("no re-encoding", StringComparison.Ordinal));
                    }
    }

    [Test]
    public static void Mp3FilenameQualityUsesTargetBitrateInsteadOfSourceBitrate()
    {
        using var viewModel = new MainViewModel
        {
            FileNameTemplateText = "{quality} {container}"
        };
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));
        var metadata = new VideoMetadata
        {
            Id = videoId,
            Title = "Fixture",
            Formats = []
        };
        var source = Audio(140, MediaContainer.WebM, AudioCodec.Opus, 143_000, 48_000);
        var render = typeof(MainViewModel).GetMethod(
            "RenderFileName",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, "RenderFileName");

        var result = (Result<string>)render.Invoke(viewModel,
            [metadata, new FormatItemViewModel(source), null, 2, OutputProfile.Mp3(192)])!;

        Assert.True(result.IsSuccess);
        Assert.Equal("192kbps mp3", result.Value);
    }

    [Test]
    public static void ConvertedAudioFilenamesUseSelectedProfileExtensionAndQuality()
    {
        using var viewModel = new MainViewModel
        {
            FileNameTemplateText = "{quality} {container}"
        };
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));
        var metadata = new VideoMetadata { Id = videoId, Title = "Fixture", Formats = [] };
        var source = new FormatItemViewModel(Audio(140, MediaContainer.WebM, AudioCodec.Opus, 143_000, 48_000));
        var render = typeof(MainViewModel).GetMethod(
            "RenderFileName",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, "RenderFileName");

        foreach (var testCase in new[]
                 {
                     (OutputProfile.Aac(256), "256kbps m4a"),
                     (OutputProfile.Opus(160), "160kbps ogg"),
                     (OutputProfile.Wav, "lossless wav"),
                     (OutputProfile.Flac, "lossless flac")
                 })
        {
            var result = (Result<string>)render.Invoke(
                viewModel,
                [metadata, source, null, 2, testCase.Item1])!;
            Assert.True(result.IsSuccess);
            Assert.Equal(testCase.Item2, result.Value);
        }
    }

    [Test]
    public static void VideoPresetSelectionUsesPersistableProfileAndTruthfulFilename()
    {
        using var viewModel = new MainViewModel
        {
            FileNameTemplateText = "{quality} {container}"
        };
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));
        var metadata = new VideoMetadata { Id = videoId, Title = "Fixture", Formats = [] };
        var selection = new FormatItemViewModel(Progressive(18, 1080, 30));
        var profileFor = typeof(MainViewModel).GetMethod(
            "OutputProfileFor",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, "OutputProfileFor");
        var render = typeof(MainViewModel).GetMethod(
            "RenderFileName",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, "RenderFileName");

        foreach (var option in viewModel.VideoProcessingOptions)
        {
            viewModel.SelectedVideoProcessing = option;
            var selected = (OutputProfile)profileFor.Invoke(viewModel, [selection])!;
            Assert.Equal(option.Value.ForVideoHeight(1080), selected);
            Assert.True(OutputProfile.TryParseIdentity(selected.Identity, out var parsed));
            Assert.Equal(selected, parsed);
        }

        var filename = (Result<string>)render.Invoke(
            viewModel,
            [metadata, selection, null, 2, OutputProfile.H264AacMp4])!;
        Assert.True(filename.IsSuccess);
        Assert.Equal("1080p mp4", filename.Value);
    }

    [Test]
    public static void QuickPresetsApplyTruthfulStateAndManualChangesBecomeCustom()
    {
        using var viewModel = new MainViewModel();
        SetFormats(viewModel, BuildCompleteCatalog());

        Assert.Equal(5, viewModel.DownloadPresets.Count);
        foreach (var preset in viewModel.DownloadPresets.Where(option =>
                     option.Value != DownloadPresetKind.Custom))
        {
            viewModel.SelectedDownloadPreset = preset;

            Assert.Equal(preset, viewModel.SelectedDownloadPreset);
            Assert.True(viewModel.SelectedFormat is not null);
            switch (preset.Value)
            {
                case DownloadPresetKind.BestOriginal:
                    Assert.Equal(DownloadMode.AudioVideo, viewModel.SelectedDownloadMode.Value);
                    Assert.Equal(OutputProfileKind.Native, viewModel.SelectedVideoProcessing.Value.Kind);
                    break;
                case DownloadPresetKind.WindowsCompatibleMp4:
                    Assert.Equal(DownloadMode.AudioVideo, viewModel.SelectedDownloadMode.Value);
                    Assert.Equal(OutputProfileKind.H264AacMp4, viewModel.SelectedVideoProcessing.Value.Kind);
                    break;
                case DownloadPresetKind.SmallFile:
                    Assert.Equal(DownloadMode.AudioVideo, viewModel.SelectedDownloadMode.Value);
                    Assert.Equal(OutputProfileKind.H265AacMp4, viewModel.SelectedVideoProcessing.Value.Kind);
                    Assert.Equal(720, viewModel.SelectedResolution?.Value);
                    break;
                case DownloadPresetKind.Mp3_320:
                    Assert.Equal(DownloadMode.AudioOnly, viewModel.SelectedDownloadMode.Value);
                    Assert.Equal(OutputProfile.Mp3(320), viewModel.SelectedAudioProcessing.Value);
                    break;
            }
        }

        viewModel.SelectedDownloadPreset = viewModel.DownloadPresets.First(option =>
            option.Value == DownloadPresetKind.WindowsCompatibleMp4);
        viewModel.SelectedVideoProcessing = viewModel.VideoProcessingOptions.First(option =>
            option.Value.Kind == OutputProfileKind.Native);
        Assert.Equal(DownloadPresetKind.Custom, viewModel.SelectedDownloadPreset.Value);
    }

    [Test]
    public static void SoftSubtitleSelectionRequiresSupportedVideoOutputAndResetsForAudioOnly()
    {
        using var viewModel = new MainViewModel();
        SetFormats(viewModel, BuildCompleteCatalog());
        viewModel.SelectedCaptionTrack = new CaptionTrackOption(new CaptionTrack
        {
            Url = new Uri("https://www.youtube.com/api/timedtext?v=Fixture123_&lang=en"),
            LanguageCode = "en",
            Name = "English"
        });

        Assert.True(viewModel.CanEmbedSelectedCaption);
        viewModel.EmbedSelectedCaption = true;
        Assert.True(viewModel.EmbedSelectedCaption);

        viewModel.SelectedDownloadMode = viewModel.DownloadModes.First(option =>
            option.Value == DownloadMode.AudioOnly);

        Assert.False(viewModel.CanEmbedSelectedCaption);
        Assert.False(viewModel.EmbedSelectedCaption);
    }

    [Test]
    public static void ChapterEmbeddingRequiresTimedChaptersAndResetsForAudioOnly()
    {
        using var viewModel = new MainViewModel();
        SetFormats(viewModel, BuildCompleteCatalog());
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));
        SetMetadata(viewModel, new VideoMetadata
        {
            Id = videoId,
            Title = "Fixture",
            Duration = TimeSpan.FromMinutes(2),
            Formats = BuildCompleteCatalog(),
            Chapters =
            [
                new VideoChapter { Title = "Intro", StartTime = TimeSpan.Zero },
                new VideoChapter { Title = "Main", StartTime = TimeSpan.FromMinutes(1) }
            ]
        });

        Assert.True(viewModel.HasChapters);
        Assert.True(viewModel.CanEmbedChapters);
        Assert.True(viewModel.CanSplitChapters);
        viewModel.EmbedChapters = true;
        viewModel.SplitChapters = true;
        Assert.True(viewModel.EmbedChapters);
        Assert.True(viewModel.SplitChapters);

        viewModel.SelectedDownloadMode = viewModel.DownloadModes.First(option =>
            option.Value == DownloadMode.VideoOnly);
        Assert.True(viewModel.CanEmbedChapters);
        Assert.True(viewModel.CanSplitChapters);
        Assert.True(viewModel.EmbedChapters);
        Assert.True(viewModel.SplitChapters);

        viewModel.SelectedDownloadMode = viewModel.DownloadModes.First(option =>
            option.Value == DownloadMode.AudioOnly);

        Assert.False(viewModel.CanEmbedChapters);
        Assert.False(viewModel.CanSplitChapters);
        Assert.False(viewModel.EmbedChapters);
        Assert.False(viewModel.SplitChapters);
    }

    private static void ExerciseAudioOnly(MainViewModel viewModel, ref int terminalCombinations)
    {
        foreach (var bitrate in viewModel.BitrateOptions.ToArray())
        {
            viewModel.SelectedBitrate = bitrate;
            foreach (var container in viewModel.ContainerOptions.ToArray())
            {
                viewModel.SelectedContainer = container;
                foreach (var codec in viewModel.AudioCodecOptions.ToArray())
                {
                    viewModel.SelectedAudioCodec = codec;
                    foreach (var processing in viewModel.AudioProcessingOptions)
                    {
                        viewModel.SelectedAudioProcessing = processing;
                        AssertTerminalOutputs(viewModel, DownloadMode.AudioOnly);
                        terminalCombinations++;
                    }
                }
            }
        }
    }

    private static void ExerciseVideo(
        MainViewModel viewModel,
        bool includeAudioFilters,
        ref int terminalCombinations)
    {
        foreach (var resolution in viewModel.ResolutionOptions.ToArray())
        {
            viewModel.SelectedResolution = resolution;
            foreach (var container in viewModel.ContainerOptions.ToArray())
            {
                viewModel.SelectedContainer = container;
                foreach (var codec in viewModel.VideoCodecOptions.ToArray())
                {
                    viewModel.SelectedVideoCodec = codec;
                    foreach (var frameRate in viewModel.FrameRateOptions.ToArray())
                    {
                        viewModel.SelectedFrameRate = frameRate;
                        foreach (var dynamicRange in viewModel.DynamicRangeOptions.ToArray())
                        {
                            viewModel.SelectedDynamicRange = dynamicRange;
                            if (includeAudioFilters)
                            {
                                ExerciseMuxedAudioFilters(viewModel, ref terminalCombinations);
                            }
                            else
                            {
                                AssertTerminalOutputs(viewModel, DownloadMode.VideoOnly);
                                terminalCombinations++;
                            }
                        }
                    }
                }
            }
        }
    }

    private static void ExerciseMuxedAudioFilters(MainViewModel viewModel, ref int terminalCombinations)
    {
        foreach (var bitrate in viewModel.BitrateOptions.ToArray())
        {
            viewModel.SelectedBitrate = bitrate;
            foreach (var codec in viewModel.AudioCodecOptions.ToArray())
            {
                viewModel.SelectedAudioCodec = codec;
                foreach (var processing in viewModel.VideoProcessingOptions)
                {
                    viewModel.SelectedVideoProcessing = processing;
                    AssertTerminalOutputs(viewModel, DownloadMode.AudioVideo);
                    terminalCombinations++;
                }
            }
        }
    }

    private static void AssertTerminalOutputs(MainViewModel viewModel, DownloadMode mode)
    {
        Assert.True(viewModel.Formats.Count > 0,
            $"Mode {mode} produced an empty terminal option path.");
        Assert.True(viewModel.SelectedFormat is not null);
        foreach (var output in viewModel.Formats)
        {
            switch (mode)
            {
                case DownloadMode.AudioOnly:
                    Assert.Equal(StreamKind.AudioOnly, output.Format.Kind);
                    break;
                case DownloadMode.VideoOnly:
                    Assert.Equal(StreamKind.VideoOnly, output.Format.Kind);
                    Assert.True(output.AudioFormat is null);
                    break;
                case DownloadMode.AudioVideo:
                    Assert.True(output.Format.Kind is StreamKind.Progressive or StreamKind.VideoOnly);
                    if (output.Format.Kind == StreamKind.VideoOnly)
                    {
                        Assert.True(output.AudioFormat is not null);
                        Assert.True(AdaptiveFormatSelector.AreMkvMuxCompatible(output.Format, output.AudioFormat!));
                    }

                    break;
            }
        }
    }

    private static void SetFormats(MainViewModel viewModel, IReadOnlyList<StreamFormat> formats)
    {
        var field = typeof(MainViewModel).GetField("_allFormats", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(MainViewModel).FullName, "_allFormats");
        field.SetValue(viewModel, formats);
        var refresh = typeof(MainViewModel).GetMethod(
            "RefreshDownloadModes",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, "RefreshDownloadModes");
        refresh.Invoke(viewModel, null);
        viewModel.SelectedDownloadMode = viewModel.DownloadModes[1];
        viewModel.SelectedDownloadMode = viewModel.DownloadModes[0];
    }

    private static void SetMetadata(MainViewModel viewModel, VideoMetadata metadata)
    {
        var field = typeof(MainViewModel).GetField("_metadata", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(MainViewModel).FullName, "_metadata");
        field.SetValue(viewModel, metadata);
    }

    private static IReadOnlyList<StreamFormat> BuildCompleteCatalog()
    {
        var formats = new List<StreamFormat>();
        var id = 1;
        foreach (var height in new[] { 360, 720 })
            foreach (var frameRate in new[] { 30, 60 })
            {
                formats.Add(Progressive(id++, height, frameRate));
            }

        foreach (var container in new[] { MediaContainer.Mp4, MediaContainer.WebM })
            foreach (var codec in new[] { VideoCodec.H264, VideoCodec.Vp9, VideoCodec.Av1 })
                foreach (var height in new[] { 720, 1080, 2160 })
                    foreach (var frameRate in new[] { 30, 60 })
                        foreach (var isHdr in new[] { false, true })
                        {
                            formats.Add(Video(id++, container, codec, height, frameRate, isHdr));
                        }

        foreach (var container in new[] { MediaContainer.Mp4, MediaContainer.WebM })
            foreach (var codec in new[] { AudioCodec.Aac, AudioCodec.Opus, AudioCodec.Vorbis })
                foreach (var bitrate in new[] { 128_000L, 192_000L, 256_000L })
                    foreach (var sampleRate in new[] { 44_100, 48_000 })
                    {
                        formats.Add(Audio(id++, container, codec, bitrate, sampleRate));
                    }

        return formats;
    }

    private static StreamFormat Progressive(int id, int height, int frameRate) => new()
    {
        FormatId = id,
        Url = new Uri($"https://example.test/{id}"),
        Kind = StreamKind.Progressive,
        Container = MediaContainer.Mp4,
        Height = height,
        FramesPerSecond = frameRate,
        Bitrate = height * 10_000L,
        VideoCodec = VideoCodec.H264,
        AudioCodec = AudioCodec.Aac,
        ContentLength = height * 100_000L
    };

    private static StreamFormat Video(
        int id,
        MediaContainer container,
        VideoCodec codec,
        int height,
        int frameRate,
        bool isHdr) => new()
        {
            FormatId = id,
            Url = new Uri($"https://example.test/{id}"),
            Kind = StreamKind.VideoOnly,
            Container = container,
            Height = height,
            FramesPerSecond = frameRate,
            IsHdr = isHdr,
            Bitrate = height * frameRate * 100L,
            VideoCodec = codec,
            AudioCodec = AudioCodec.None,
            ContentLength = height * frameRate * 1_000L
        };

    private static StreamFormat Audio(
        int id,
        MediaContainer container,
        AudioCodec codec,
        long bitrate,
        int sampleRate) => new()
        {
            FormatId = id,
            Url = new Uri($"https://example.test/{id}"),
            Kind = StreamKind.AudioOnly,
            Container = container,
            Bitrate = bitrate,
            VideoCodec = VideoCodec.None,
            AudioCodec = codec,
            AudioSampleRate = sampleRate,
            ContentLength = bitrate
        };
}
