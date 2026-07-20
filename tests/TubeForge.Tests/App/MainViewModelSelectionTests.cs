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
            [metadata, new FormatItemViewModel(source), null, 2, AudioOutputProfile.Mp3(192)])!;

        Assert.True(result.IsSuccess);
        Assert.Equal("192kbps mp3", result.Value);
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
                AssertTerminalOutputs(viewModel, DownloadMode.AudioVideo);
                terminalCombinations++;
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
