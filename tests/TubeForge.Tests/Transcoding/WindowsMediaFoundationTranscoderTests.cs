using TubeForge.Core.Media;
using TubeForge.Tests.Framework;
using TubeForge.Transcoding;

namespace TubeForge.Tests.Transcoding;

public static class WindowsMediaFoundationTranscoderTests
{
    [Test]
    public static async Task ConvertsPcmWaveToValidatedMp3()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.wav");
            var destination = Path.Combine(directory, "output.mp3");
            WriteWave(source, TimeSpan.FromMilliseconds(350));

            var result = await new WindowsMediaFoundationTranscoder().TranscodeAsync(new AudioTranscodeRequest
            {
                SourcePath = source,
                DestinationPath = destination,
                Output = AudioOutputProfile.Mp3(192)
            });

            Assert.True(result.IsSuccess, result.Error?.TechnicalDetail ?? result.Error?.Message);
            Assert.Equal(192, result.Value.BitrateKbps);
            Assert.Equal(2, result.Value.Channels);
            Assert.Equal(44_100, result.Value.SampleRate);
            Assert.True(result.Value.BytesWritten > 0);
            Assert.True(Mp3FileValidator.Validate(destination).IsSuccess);
            Assert.False(File.Exists(destination + ".transcoding.mp3"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public static async Task CancelledConversionPublishesNoOutput()
    {
        var directory = CreateTemporaryDirectory();
        try
        {
            var source = Path.Combine(directory, "source.wav");
            var destination = Path.Combine(directory, "output.mp3");
            WriteWave(source, TimeSpan.FromMilliseconds(100));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            var result = await new WindowsMediaFoundationTranscoder().TranscodeAsync(new AudioTranscodeRequest
            {
                SourcePath = source,
                DestinationPath = destination,
                Output = AudioOutputProfile.Mp3(320)
            }, cancellation.Token);

            Assert.False(result.IsSuccess);
            Assert.Equal("Operation.Cancelled", result.Error!.Code);
            Assert.False(File.Exists(destination));
            Assert.False(File.Exists(destination + ".transcoding.mp3"));
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
            var source = Path.Combine(directory, "source.wav");
            var destination = Path.Combine(directory, "output.mp3");
            WriteWave(source, TimeSpan.FromMilliseconds(350));
            var transcoder = new WindowsMediaFoundationTranscoder();
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

            var recovered = await transcoder.TranscodeAsync(request);

            Assert.True(recovered.IsSuccess, recovered.Error?.Message);
            Assert.Equal(originalBytes.LongLength, recovered.Value.BytesWritten);
            Assert.SequenceEqual(originalBytes, await File.ReadAllBytesAsync(destination));
            Assert.False(File.Exists(destination + ".transcoding.mp3"));
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

    private static void WriteWave(string path, TimeSpan duration)
    {
        const int sampleRate = 44_100;
        const short channels = 2;
        const short bitsPerSample = 16;
        var frameCount = checked((int)Math.Ceiling(duration.TotalSeconds * sampleRate));
        var blockAlignment = checked((short)(channels * bitsPerSample / 8));
        var dataLength = checked(frameCount * blockAlignment);

        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(checked(36 + dataLength));
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(checked(sampleRate * blockAlignment));
        writer.Write(blockAlignment);
        writer.Write(bitsPerSample);
        writer.Write("data"u8);
        writer.Write(dataLength);

        for (var frame = 0; frame < frameCount; frame++)
        {
            var sample = checked((short)(Math.Sin(frame * 2 * Math.PI * 440 / sampleRate) * short.MaxValue * 0.15));
            writer.Write(sample);
            writer.Write(sample);
        }
    }
}
