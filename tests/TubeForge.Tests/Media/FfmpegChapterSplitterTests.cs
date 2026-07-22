using TubeForge.Core.Files;
using TubeForge.Core.Media;
using TubeForge.Media;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Media;

public static class FfmpegChapterSplitterTests
{
    [Test]
    public static async Task SplitsEveryChapterIntoAtomicSanitizedStreamCopyOutputs()
    {
        using var directory = new MediaTestDirectory();
        var executable = Path.Combine(directory.Path, "ffmpeg.exe");
        var source = Path.Combine(directory.Path, "source.mp4");
        var outputDirectory = Path.Combine(directory.Path, "Fixture - chapters");
        await File.WriteAllBytesAsync(executable, []);
        await File.WriteAllBytesAsync(source, SyntheticMp4.Track(
            "vide", "VIDEO"u8, 1, 90_000, 90_000));
        var runner = new CopyingRunner(source);
        var request = Request(source, outputDirectory);

        var result = await new FfmpegChapterSplitter(executable, runner).SplitAsync(request);

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.Equal(2, result.Value.OutputPaths.Count);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "01 - Intro.mp4")));
        Assert.True(File.Exists(Path.Combine(outputDirectory, "02 - Main topic.mp4")));
        Assert.Equal(2, runner.Calls.Count);
        foreach (var arguments in runner.Calls)
        {
            Assert.True(ContainsAdjacent(arguments, "-c", "copy"));
            Assert.True(ContainsAdjacent(arguments, "-avoid_negative_ts", "make_zero"));
            Assert.True(ContainsAdjacent(arguments, "-movflags", "+faststart"));
        }

        Assert.Equal(0, Directory.GetDirectories(directory.Path, "*.processing").Length);

        File.Delete(source);
        var recovered = await new FfmpegChapterSplitter(executable, runner).SplitAsync(
            request with { AllowExistingValidatedOutput = true });
        Assert.True(recovered.IsSuccess, recovered.Error?.Message);
        Assert.Equal(2, runner.Calls.Count);

        File.Move(
            Path.Combine(outputDirectory, "02 - Main topic.mp4"),
            Path.Combine(outputDirectory, "unexpected.mp4"));
        var rejected = await new FfmpegChapterSplitter(executable, runner).SplitAsync(
            request with { AllowExistingValidatedOutput = true });
        Assert.False(rejected.IsSuccess);
        Assert.Equal("Media.ChapterSplitValidationFailed", rejected.Error?.Code);
    }

    [Test]
    public static async Task FailedSplitPublishesNoChapterFolder()
    {
        using var directory = new MediaTestDirectory();
        var executable = Path.Combine(directory.Path, "ffmpeg.exe");
        var source = Path.Combine(directory.Path, "source.mp4");
        var outputDirectory = Path.Combine(directory.Path, "Fixture - chapters");
        await File.WriteAllBytesAsync(executable, []);
        await File.WriteAllBytesAsync(source, SyntheticMp4.Track(
            "vide", "VIDEO"u8, 1, 90_000, 90_000));

        var result = await new FfmpegChapterSplitter(executable, new FailingRunner()).SplitAsync(
            Request(source, outputDirectory));

        Assert.False(result.IsSuccess);
        Assert.Equal("Media.ChapterSplitFailed", result.Error?.Code);
        Assert.False(Directory.Exists(outputDirectory));
        Assert.Equal(0, Directory.GetDirectories(directory.Path, "*.processing").Length);
    }

    private static ChapterSplitRequest Request(string source, string outputDirectory) => new()
    {
        SourcePath = source,
        DestinationDirectory = outputDirectory,
        OutputContainer = MediaContainer.Mp4,
        Chapters =
        [
            new VideoChapter { Title = "Intro", StartTime = TimeSpan.Zero },
            new VideoChapter { Title = "Main: topic", StartTime = TimeSpan.FromSeconds(30) }
        ],
        Duration = TimeSpan.FromMinutes(1),
        FileNameContext = new FileNameTemplateContext
        {
            Title = "Fixture",
            Channel = "Channel",
            VideoId = "Fixture123_",
            Quality = "1080p",
            Container = "mp4",
            IndexWidth = 2
        }
    };

    private static bool ContainsAdjacent(IReadOnlyList<string> arguments, string first, string second)
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

    private sealed class CopyingRunner(string sourcePath) : IFfmpegProcessRunner
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public async Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            Calls.Add(arguments.ToArray());
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

    private sealed class FailingRunner : IFfmpegProcessRunner
    {
        public Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken) => Task.FromResult(29);
    }
}
