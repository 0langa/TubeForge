using System.Text.Json;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.Tests.Framework;
using TubeForge.YouTube.Sidecars;

namespace TubeForge.Tests.YouTube;

public static class MetadataSidecarWriterTests
{
    [Test]
    public static async Task WritesStableMetadataWithoutEphemeralStreamUrls()
    {
        using var directory = new TestDirectory();
        Assert.True(YouTubeVideoId.TryCreate("Fixture123_", out var videoId));
        var metadata = new VideoMetadata
        {
            Id = videoId,
            Title = "Fixture title",
            Channel = "Fixture channel",
            Duration = TimeSpan.FromSeconds(123),
            ThumbnailUrl = new Uri("https://i.ytimg.com/vi/Fixture123_/hqdefault.jpg"),
            Formats =
            [
                new StreamFormat
                {
                    FormatId = 137,
                    Url = new Uri("https://video.googlevideo.com/videoplayback?sig=secret-signature"),
                    Container = MediaContainer.Mp4,
                    Kind = StreamKind.VideoOnly,
                    VideoCodec = VideoCodec.H264,
                    Width = 1920,
                    Height = 1080,
                    FramesPerSecond = 30,
                    QualityLabel = "1080p"
                }
            ],
            CaptionTracks =
            [
                new CaptionTrack
                {
                    Url = new Uri("https://www.youtube.com/api/timedtext?v=Fixture123_&sig=secret-caption"),
                    LanguageCode = "en",
                    Name = "English"
                }
            ],
            Chapters =
            [
                new VideoChapter
                {
                    Title = "Introduction",
                    StartTime = TimeSpan.Zero
                }
            ]
        };
        var destination = Path.Combine(directory.Path, "fixture.info.json");

        var result = await MetadataSidecarWriter.WriteAsync(metadata, destination);

        Assert.True(result.IsSuccess, result.Error?.Message);
        var json = await File.ReadAllTextAsync(destination);
        Assert.False(json.Contains("secret-signature", StringComparison.Ordinal));
        Assert.False(json.Contains("secret-caption", StringComparison.Ordinal));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("Fixture123_", root.GetProperty("videoId").GetString());
        Assert.Equal("https://www.youtube.com/watch?v=Fixture123_", root.GetProperty("sourceUrl").GetString());
        Assert.Equal(137, root.GetProperty("formats")[0].GetProperty("formatId").GetInt32());
        Assert.Equal("en", root.GetProperty("captions")[0].GetProperty("languageCode").GetString());
        Assert.Equal("Introduction", root.GetProperty("chapters")[0].GetProperty("title").GetString());
        Assert.Equal(0d, root.GetProperty("chapters")[0].GetProperty("startSeconds").GetDouble());
        Assert.False(File.Exists(destination + ".part"));
    }

    private sealed class TestDirectory : IDisposable
    {
        private static readonly string SafeRoot = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "TubeForge.Tests"));

        public TestDirectory()
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
