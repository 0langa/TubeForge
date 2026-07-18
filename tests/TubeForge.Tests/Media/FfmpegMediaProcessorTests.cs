using TubeForge.Core.Media;
using TubeForge.Media;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Media;

public static class FfmpegMediaProcessorTests
{
    [Test]
    public static async Task MuxUsesStreamCopyFastStartAndPublishesConventionalMp4()
    {
        using var directory = new MediaTestDirectory();
        var executable = Path.Combine(directory.Path, "ffmpeg.exe");
        var video = Path.Combine(directory.Path, "video.mp4");
        var audio = Path.Combine(directory.Path, "audio.m4a");
        var output = Path.Combine(directory.Path, "output.mp4");
        await File.WriteAllBytesAsync(executable, []);
        await File.WriteAllBytesAsync(video, SyntheticMp4.Track(
            "vide", "VIDEO"u8, 1, 90_000, 90_000));
        await File.WriteAllBytesAsync(audio, SyntheticMp4.Track(
            "soun", "AUDIO"u8, 1, 48_000, 48_000));
        var runner = new CopyingProcessRunner(video);
        var processor = new FfmpegMediaProcessor(executable, runner);

        var result = await processor.MuxMp4Async(video, audio, output);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(File.Exists(output));
        Assert.Equal(2, runner.Arguments.Count(argument => argument == "-i"));
        Assert.True(ContainsAdjacent(runner.Arguments, "-c", "copy"));
        Assert.True(ContainsAdjacent(runner.Arguments, "-movflags", "+faststart"));
        Assert.True(ContainsAdjacent(runner.Arguments, "-map", "0:v:0"));
        Assert.True(ContainsAdjacent(runner.Arguments, "-map", "1:a:0"));
        Assert.True(runner.Arguments.Contains("-nostdin"));
        Assert.True(runner.Arguments.Contains("-xerror"));
    }

    [Test]
    public static async Task MuxWebMUsesStreamCopyWithoutFastStartAndPublishesWebM()
    {
        using var directory = new MediaTestDirectory();
        var executable = Path.Combine(directory.Path, "ffmpeg.exe");
        var video = Path.Combine(directory.Path, "video.webm");
        var audio = Path.Combine(directory.Path, "audio.webm");
        var output = Path.Combine(directory.Path, "output.webm");
        var muxed = Path.Combine(directory.Path, "muxed-source.webm");
        await File.WriteAllBytesAsync(executable, []);
        await File.WriteAllBytesAsync(video, SyntheticWebM.Track(1, "V_VP9", (0, "VIDEO"u8.ToArray())));
        await File.WriteAllBytesAsync(audio, SyntheticWebM.Track(2, "A_OPUS", (0, "AUDIO"u8.ToArray())));
        await File.WriteAllBytesAsync(muxed, SyntheticWebM.Track(
            1, "V_VP9", (0, "VIDEO"u8.ToArray()), (100, "AUDIO"u8.ToArray())));
        var runner = new CopyingProcessRunner(muxed);
        var processor = new FfmpegMediaProcessor(executable, runner);

        var result = await processor.MuxAsync(video, audio, output, MediaContainer.WebM);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(File.Exists(output));
        Assert.Equal(2, runner.Arguments.Count(argument => argument == "-i"));
        Assert.True(ContainsAdjacent(runner.Arguments, "-c", "copy"));
        Assert.True(ContainsAdjacent(runner.Arguments, "-f", "webm"));
        Assert.False(runner.Arguments.Contains("+faststart"));
    }

    [Test]
    public static async Task MuxMkvUsesMatroskaStreamCopyAndValidatesEbml()
    {
        using var directory = new MediaTestDirectory();
        var executable = Path.Combine(directory.Path, "ffmpeg.exe");
        var video = Path.Combine(directory.Path, "video.mp4");
        var audio = Path.Combine(directory.Path, "audio.webm");
        var output = Path.Combine(directory.Path, "output.mkv");
        var muxed = Path.Combine(directory.Path, "muxed-source.mkv");
        await File.WriteAllBytesAsync(executable, []);
        await File.WriteAllBytesAsync(video, SyntheticMp4.Track("vide", "VIDEO"u8, 1, 90_000, 90_000));
        await File.WriteAllBytesAsync(audio, SyntheticWebM.Track(2, "A_OPUS", (0, "AUDIO"u8.ToArray())));
        await File.WriteAllBytesAsync(muxed, SyntheticWebM.Track(
            1, "V_VP9", (0, "VIDEO"u8.ToArray()), (100, "AUDIO"u8.ToArray())));
        var runner = new CopyingProcessRunner(muxed);
        var processor = new FfmpegMediaProcessor(executable, runner);

        var result = await processor.MuxAsync(video, audio, output, MediaContainer.Mkv);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(File.Exists(output));
        Assert.True(ContainsAdjacent(runner.Arguments, "-c", "copy"));
        Assert.True(ContainsAdjacent(runner.Arguments, "-f", "matroska"));
        Assert.False(runner.Arguments.Contains("+faststart"));
    }

    [Test]
    public static async Task RejectsFragmentedProcessorOutputWithoutPublishingDestination()
    {
        using var directory = new MediaTestDirectory();
        var executable = Path.Combine(directory.Path, "ffmpeg.exe");
        var source = Path.Combine(directory.Path, "fragmented.mp4");
        var output = Path.Combine(directory.Path, "output.mp4");
        await File.WriteAllBytesAsync(executable, []);
        await File.WriteAllBytesAsync(source, SyntheticMp4.FragmentedTrack(
            "vide", 1, 1000, (0, "VIDEO"u8.ToArray())));
        var processor = new FfmpegMediaProcessor(executable, new CopyingProcessRunner(source));

        var result = await processor.RemuxMp4Async(source, output);

        Assert.False(result.IsSuccess);
        Assert.Equal("Media.IncompatibleMp4Layout", result.Error?.Code);
        Assert.False(File.Exists(output));
        Assert.Equal(0, Directory.GetFiles(directory.Path, "*.processing.mp4").Length);
    }

    [Test]
    public static async Task FailsClosedWhenBundledFfmpegIsMissing()
    {
        using var directory = new MediaTestDirectory();
        var source = Path.Combine(directory.Path, "source.mp4");
        var output = Path.Combine(directory.Path, "output.mp4");
        await File.WriteAllBytesAsync(source, SyntheticMp4.Track(
            "vide", "VIDEO"u8, 1, 90_000, 90_000));
        var processor = new FfmpegMediaProcessor(
            Path.Combine(directory.Path, "missing-ffmpeg.exe"),
            new CopyingProcessRunner(source));

        var result = await processor.RemuxMp4Async(source, output);

        Assert.False(result.IsSuccess);
        Assert.Equal("Media.FFmpegMissing", result.Error?.Code);
        Assert.False(File.Exists(output));
    }

    [Test]
    public static async Task RecoversValidatedOutputPublishedBeforeQueueCheckpoint()
    {
        using var directory = new MediaTestDirectory();
        var executable = Path.Combine(directory.Path, "ffmpeg.exe");
        var source = Path.Combine(directory.Path, "source.mp4");
        var output = Path.Combine(directory.Path, "output.mp4");
        await File.WriteAllBytesAsync(executable, []);
        await File.WriteAllBytesAsync(source, SyntheticMp4.Track(
            "vide", "VIDEO"u8, 1, 90_000, 90_000));
        var runner = new CopyingProcessRunner(source);
        var processor = new FfmpegMediaProcessor(executable, runner);
        var first = await processor.RemuxMp4Async(source, output);
        Assert.True(first.IsSuccess, first.Error?.Message);
        File.Delete(source);

        var recovered = await processor.RemuxMp4Async(
            source,
            output,
            allowExistingValidatedOutput: true);

        Assert.True(recovered.IsSuccess, recovered.Error?.Message);
        Assert.Equal(first.Value.BytesWritten, recovered.Value.BytesWritten);
    }

    private static bool ContainsAdjacent(
        IReadOnlyList<string> arguments,
        string first,
        string second)
    {
        for (var index = 0; index + 1 < arguments.Count; index++)
        {
            if (arguments[index] == first && arguments[index + 1] == second)
            {
                return true;
            }
        }

        return false;
    }

    private sealed class CopyingProcessRunner(string sourcePath) : IFfmpegProcessRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public async Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            Arguments = arguments.ToArray();
            await using var source = File.OpenRead(sourcePath);
            await using var destination = new FileStream(
                arguments[^1],
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                FileOptions.Asynchronous);
            await source.CopyToAsync(destination, cancellationToken);
            return 0;
        }
    }
}
