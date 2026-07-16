using System.Net;
using TubeForge.Core.Media;
using TubeForge.Downloads;
using TubeForge.Tests.Framework;
using TubeForge.Tests.Media;

namespace TubeForge.Tests.Downloads;

public static class AdaptiveDownloadEngineTests
{
    [Test]
    public static async Task DownloadsBothTracksMuxesAndRemovesIntermediates()
    {
        using var directory = new AdaptiveTestDirectory();
        var video = SyntheticMp4.Track("vide", "VIDEO-SAMPLES"u8, 1, 90_000, 450_000);
        var audio = SyntheticMp4.Track("soun", "AUDIO-SAMPLES"u8, 1, 48_000, 240_000);
        using var handler = new MediaHandler(video, audio);
        using var client = new HttpClient(handler);
        var direct = new DirectDownloadEngine(
            client,
            DownloadUriPolicy.YouTubeMediaAndLoopback,
            (_, _) => Task.CompletedTask);
        var engine = new AdaptiveDownloadEngine(direct);
        var output = Path.Combine(directory.Path, "combined.mp4");
        var videoTrack = output + ".video-track.mp4";
        var audioTrack = output + ".audio-track.m4a";

        var result = await engine.DownloadAsync(new AdaptiveDownloadRequest
        {
            Video = Request("video", videoTrack, video.Length, MediaContainer.Mp4),
            Audio = Request("audio", audioTrack, audio.Length, MediaContainer.Mp4),
            DestinationPath = output,
            OutputContainer = MediaContainer.Mp4
        });

        Assert.True(result.IsSuccess, result.Error?.Message);
        Assert.True(File.Exists(output));
        Assert.False(File.Exists(videoTrack));
        Assert.False(File.Exists(audioTrack));
        Assert.Equal(new FileInfo(output).Length, result.Value.BytesWritten);
        Assert.True(result.Value.BytesWritten > Math.Max(video.Length, audio.Length));
    }

    private static DownloadRequest Request(
        string kind,
        string destination,
        long length,
        MediaContainer container) => new()
        {
            SourceUrl = new Uri($"http://localhost/{kind}"),
            SourceIdentity = $"Fixture123_:{kind}",
            DestinationPath = destination,
            ExpectedLength = length,
            ExpectedContainer = container
        };

    private sealed class MediaHandler(byte[] video, byte[] audio) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var payload = request.RequestUri?.AbsolutePath == "/video" ? video : audio;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload)
            });
        }
    }

    private sealed class AdaptiveTestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests"));

        public AdaptiveTestDirectory()
        {
            Path = System.IO.Path.Combine(SafeRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            var resolved = System.IO.Path.GetFullPath(Path);
            if (!resolved.StartsWith(SafeRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to clean a test directory outside the safe root.");
            }

            if (Directory.Exists(resolved))
            {
                Directory.Delete(resolved, recursive: true);
            }
        }
    }
}
