using TubeForge.Core.YouTube;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class YouTubeUrlParserTests
{
    private const string Id = "dQw4w9WgXcQ";

    [Test]
    public static void ParsesSupportedUrlShapes()
    {
        string[] inputs =
        [
            $"https://www.youtube.com/watch?v={Id}",
            $"https://youtube.com/watch?feature=share&v={Id}&t=1",
            $"https://m.youtube.com/shorts/{Id}?feature=share",
            $"https://youtube.com/live/{Id}",
            $"https://www.youtube.com/embed/{Id}",
            $"https://www.youtube-nocookie.com/embed/{Id}",
            $"https://youtu.be/{Id}?t=42",
            $"youtu.be/{Id}"
        ];

        foreach (var input in inputs)
        {
            var result = YouTubeUrlParser.ParseVideoId(input);
            Assert.True(result.IsSuccess, $"Expected supported URL: {input}; {result.Error}");
            Assert.Equal(Id, result.Value.Value);
        }
    }

    [Test]
    public static void RejectsSpoofedAndUnsupportedHosts()
    {
        string[] inputs =
        [
            $"https://youtube.com.example.test/watch?v={Id}",
            $"https://notyoutube.com/watch?v={Id}",
            $"https://youtu.be.example.test/{Id}",
            $"https://user@youtube.com/watch?v={Id}",
            $"ftp://youtube.com/watch?v={Id}"
        ];

        foreach (var input in inputs)
        {
            Assert.False(YouTubeUrlParser.ParseVideoId(input).IsSuccess, input);
        }
    }

    [Test]
    public static void RejectsMalformedIdsAndRoutes()
    {
        string?[] inputs =
        [
            null,
            "",
            "   ",
            "not a url",
            "https://youtube.com/",
            "https://youtube.com/watch",
            "https://youtube.com/watch?v=short",
            "https://youtube.com/watch?v=dQw4w9WgXc!",
            $"https://youtube.com/channel/{Id}",
            $"https://youtube-nocookie.com/watch?v={Id}"
        ];

        foreach (var input in inputs)
        {
            Assert.False(YouTubeUrlParser.ParseVideoId(input).IsSuccess, input);
        }
    }
}
