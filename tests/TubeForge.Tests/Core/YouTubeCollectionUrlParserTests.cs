using TubeForge.Core.YouTube;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class YouTubeCollectionUrlParserTests
{
    [Test]
    public static void ParsesPlaylistAndChannelUrlShapes()
    {
        var playlist = YouTubeCollectionUrlParser.Parse(
            "https://www.youtube.com/watch?v=Video000001&list=PL1234567890");
        var shortPlaylist = YouTubeCollectionUrlParser.Parse(
            "https://youtu.be/Video000001?list=PL1234567890");
        var channel = YouTubeCollectionUrlParser.Parse(
            "https://www.youtube.com/channel/UC1234567890123456789012");
        var handle = YouTubeCollectionUrlParser.Parse("youtube.com/@TubeForge_App");
        var handleVideos = YouTubeCollectionUrlParser.Parse("https://www.youtube.com/@TubeForge_App/videos");
        var legacy = YouTubeCollectionUrlParser.Parse("https://m.youtube.com/user/TubeForge");

        Assert.True(playlist.IsSuccess, playlist.Error?.Message);
        Assert.Equal(YouTubeCollectionKind.Playlist, playlist.Value.Kind);
        Assert.Equal("https://www.youtube.com/playlist?list=PL1234567890", playlist.Value.CanonicalUrl.AbsoluteUri);
        Assert.True(shortPlaylist.IsSuccess, shortPlaylist.Error?.Message);
        Assert.True(channel.IsSuccess, channel.Error?.Message);
        Assert.Equal(YouTubeCollectionKind.Channel, channel.Value.Kind);
        Assert.True(channel.Value.CanonicalUrl.AbsolutePath.EndsWith("/videos", StringComparison.Ordinal));
        Assert.True(handle.IsSuccess, handle.Error?.Message);
        Assert.Equal("@TubeForge_App", handle.Value.Identifier);
        Assert.True(handleVideos.IsSuccess, handleVideos.Error?.Message);
        Assert.Equal(handle.Value.CanonicalUrl, handleVideos.Value.CanonicalUrl);
        Assert.True(legacy.IsSuccess, legacy.Error?.Message);
    }

    [Test]
    public static void RejectsSpoofedHostsAndUnsafeIdentifiers()
    {
        foreach (var input in new[]
                 {
                     "https://youtube.com.evil.invalid/playlist?list=PL1234567890",
                     "https://www.youtube.com/playlist?list=../escape",
                     "https://www.youtube.com/@a",
                     "https://www.youtube.com/channel/UCshort",
                     "https://www.youtube.com/@TubeForge_App/community",
                     "https://www.youtube.com/feed/subscriptions"
                 })
        {
            Assert.False(YouTubeCollectionUrlParser.Parse(input).IsSuccess, input);
        }
    }
}
