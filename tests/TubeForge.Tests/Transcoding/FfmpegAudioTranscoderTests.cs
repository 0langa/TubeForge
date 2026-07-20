using TubeForge.Core.Media;
using TubeForge.Tests.Framework;
using TubeForge.Transcoding;

namespace TubeForge.Tests.Transcoding;

public static class FfmpegAudioTranscoderTests
{
    [Test]
    public static async Task ConvertsAacAndOpusInputsAtEveryMp3Profile()
    {
        foreach (var extension in new[] { ".m4a", ".webm" })
        {
            foreach (var bitrate in new[] { 128, 192, 256, 320 })
            {
                var directory = CreateTemporaryDirectory();
                try
                {
                    var executable = Path.Combine(directory, "ffmpeg.exe");
                    var source = Path.Combine(directory, "source" + extension);
                    var destination = Path.Combine(directory, $"output-{bitrate}.mp3");
                    await File.WriteAllBytesAsync(executable, []);
                    await File.WriteAllBytesAsync(source, [0x01, 0x02, 0x03]);
                    var runner = new ValidMp3ProcessRunner();

                    var result = await new FfmpegAudioTranscoder(executable, runner).TranscodeAsync(
                        new AudioTranscodeRequest
                        {
                            SourcePath = source,
                            DestinationPath = destination,
                            Output = AudioOutputProfile.Mp3(bitrate)
                        });

                    Assert.True(result.IsSuccess, result.Error?.Message);
                    Assert.Equal(bitrate, result.Value.BitrateKbps);
                    Assert.True(result.Value.BytesWritten > 0);
                    Assert.True(Mp3FileValidator.Validate(destination).IsSuccess);
                    Assert.True(ContainsAdjacent(runner.Arguments, "-i", source));
                    Assert.True(ContainsAdjacent(runner.Arguments, "-map", "0:a:0"));
                    Assert.True(ContainsAdjacent(runner.Arguments, "-c:a", "libmp3lame"));
                    Assert.True(ContainsAdjacent(runner.Arguments, "-b:a", $"{bitrate}k"));
                    Assert.True(ContainsAdjacent(runner.Arguments, "-f", "mp3"));
                    Assert.True(runner.Arguments.Contains("-vn"));
                    Assert.True(runner.Arguments.Contains("-nostdin"));
                    Assert.True(runner.Arguments.Contains("-xerror"));
                    Assert.Equal(0, Directory.GetFiles(directory, "*.transcoding.mp3").Length);
                }
                finally
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
    }

    [Test]
    public static async Task CancelledConversionPublishesNoOutput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var executable = Path.Combine(directory, "ffmpeg.exe");
            var source = Path.Combine(directory, "source.m4a");
            var destination = Path.Combine(directory, "output.mp3");
            await File.WriteAllBytesAsync(executable, []);
            await File.WriteAllBytesAsync(source, [0x01]);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = await new FfmpegAudioTranscoder(
                executable,
                new ValidMp3ProcessRunner()).TranscodeAsync(new AudioTranscodeRequest
                {
                    SourcePath = source,
                    DestinationPath = destination,
                    Output = AudioOutputProfile.Mp3(320)
                }, cancellation.Token);

            Assert.False(result.IsSuccess);
            Assert.Equal("Operation.Cancelled", result.Error!.Code);
            Assert.False(File.Exists(destination));
            Assert.Equal(0, Directory.GetFiles(directory, "*.transcoding.mp3").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static async Task InvalidFfmpegOutputFailsClosedAndDeletesTemporaryFile()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var executable = Path.Combine(directory, "ffmpeg.exe");
            var source = Path.Combine(directory, "source.m4a");
            var destination = Path.Combine(directory, "output.mp3");
            await File.WriteAllBytesAsync(executable, []);
            await File.WriteAllBytesAsync(source, [0x01]);

            var result = await new FfmpegAudioTranscoder(
                executable,
                new InvalidOutputProcessRunner()).TranscodeAsync(new AudioTranscodeRequest
                {
                    SourcePath = source,
                    DestinationPath = destination,
                    Output = AudioOutputProfile.Mp3(192)
                });

            Assert.False(result.IsSuccess);
            Assert.Equal("Media.InvalidTranscodeOutput", result.Error!.Code);
            Assert.False(File.Exists(destination));
            Assert.Equal(0, Directory.GetFiles(directory, "*.transcoding.mp3").Length);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static async Task RecoversValidatedOutputPublishedBeforeQueueCheckpoint()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var executable = Path.Combine(directory, "ffmpeg.exe");
            var source = Path.Combine(directory, "source.m4a");
            var destination = Path.Combine(directory, "output.mp3");
            await File.WriteAllBytesAsync(executable, []);
            await File.WriteAllBytesAsync(source, [0x01]);
            var runner = new ValidMp3ProcessRunner();
            var transcoder = new FfmpegAudioTranscoder(executable, runner);
            var request = new AudioTranscodeRequest
            {
                SourcePath = source,
                DestinationPath = destination,
                Output = AudioOutputProfile.Mp3(192),
                AllowExistingValidatedOutput = true
            };
            var initial = await transcoder.TranscodeAsync(request);
            Assert.True(initial.IsSuccess, initial.Error?.Message);
            var originalBytes = await File.ReadAllBytesAsync(destination);
            var calls = runner.CallCount;

            var recovered = await transcoder.TranscodeAsync(request);

            Assert.True(recovered.IsSuccess, recovered.Error?.Message);
            Assert.Equal(originalBytes.LongLength, recovered.Value.BytesWritten);
            Assert.SequenceEqual(originalBytes, await File.ReadAllBytesAsync(destination));
            Assert.Equal(calls, runner.CallCount);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static async Task MissingFfmpegFailsBeforePublishingOutput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.m4a");
            var destination = Path.Combine(directory, "output.mp3");
            await File.WriteAllBytesAsync(source, [0x01]);

            var result = await new FfmpegAudioTranscoder(
                Path.Combine(directory, "missing-ffmpeg.exe"),
                new ValidMp3ProcessRunner()).TranscodeAsync(new AudioTranscodeRequest
                {
                    SourcePath = source,
                    DestinationPath = destination,
                    Output = AudioOutputProfile.Mp3(192)
                });

            Assert.False(result.IsSuccess);
            Assert.Equal("Media.FFmpegMissing", result.Error!.Code);
            Assert.False(File.Exists(destination));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static void ValidatorRejectsNonMp3Bytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tubeforge-invalid-{Guid.NewGuid():N}.mp3");
        try
        {
            File.WriteAllBytes(path, [0x00, 0x11, 0x22, 0x33, 0x44]);
            var result = Mp3FileValidator.Validate(path);
            Assert.False(result.IsSuccess);
            Assert.Equal("Media.InvalidTranscodeOutput", result.Error!.Code);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tubeforge-transcode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
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

    private sealed class ValidMp3ProcessRunner : IFfmpegAudioProcessRunner
    {
        private static readonly byte[] ValidMp3 = [
            0xff, 0xfb, 0x90, 0x64, 0x00, 0x00, 0x00, 0x00,
            0xff, 0xfb, 0x90, 0x64, 0x00, 0x00, 0x00, 0x00
        ];

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public int CallCount { get; private set; }

        public async Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            Arguments = arguments.ToArray();
            CallCount++;
            await File.WriteAllBytesAsync(arguments[^1], ValidMp3, cancellationToken);
            return 0;
        }
    }

    private sealed class InvalidOutputProcessRunner : IFfmpegAudioProcessRunner
    {
        public async Task<int> RunAsync(
            string executablePath,
            IReadOnlyList<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            await File.WriteAllBytesAsync(arguments[^1], [0x00, 0x11, 0x22], cancellationToken);
            return 0;
        }
    }
}
