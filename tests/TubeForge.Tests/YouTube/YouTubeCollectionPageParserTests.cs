using TubeForge.Tests.Framework;
using TubeForge.YouTube.Collections;

namespace TubeForge.Tests.YouTube;

public static class YouTubeCollectionPageParserTests
{
    [Test]
    public static void ParsesItemsTitleConfigurationAndContinuation()
    {
        var result = YouTubeCollectionPageParser.ParseInitialHtml(CollectionFixtures.InitialHtml);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.Equal("Fixture playlist", result.Value.Title);
        Assert.Equal(1, result.Value.Items.Count);
        Assert.Equal("Video000001", result.Value.Items[0].VideoId.Value);
        Assert.Equal("First video", result.Value.Items[0].Title);
        Assert.Equal(1, result.Value.Items[0].Index);
        Assert.Equal(TimeSpan.FromSeconds(61), result.Value.Items[0].Duration);
        Assert.Equal("ContinuationFixture_1", result.Value.ContinuationToken);
        Assert.Equal("FixtureKey_123", result.Value.ContinuationContext?.ApiKey);
        Assert.Equal("2.20260716.00.00", result.Value.ContinuationContext?.ClientVersion);
    }

    [Test]
    public static void RejectsMalformedPagesAndUntrustedThumbnails()
    {
        Assert.False(YouTubeCollectionPageParser.ParseInitialHtml("<html>none</html>").IsSuccess);
        var result = YouTubeCollectionPageParser.ParseContinuationJson("""
            {
              "items": [{
                "playlistVideoRenderer": {
                  "videoId": "Video000003",
                  "title": {"simpleText": "Safe title"},
                  "thumbnail": {"thumbnails": [{"url": "https://evil.invalid/image.jpg"}]}
                }
              }]
            }
            """);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.Equal<Uri?>(null, result.Value.Items[0].ThumbnailUrl);
    }

    [Test]
    public static void ParsesCurrentLockupAndContinuationViewModels()
    {
        var result = YouTubeCollectionPageParser.ParseInitialHtml("""
            <script>
            ytcfg.set({"INNERTUBE_API_KEY":"FixtureKey_123","INNERTUBE_CLIENT_VERSION":"2.20260716.00.00"});
            var ytInitialData = {"contents":[
              {"lockupViewModel":{
                "contentImage":{"thumbnailViewModel":{
                  "image":{"sources":[{"url":"https://i.ytimg.com/vi/Video000004/hqdefault.jpg"}]},
                  "overlays":[{"thumbnailBottomOverlayViewModel":{"badges":[
                    {"thumbnailBadgeViewModel":{"text":"1:05"}}
                  ]}}]
                }},
                "metadata":{"lockupMetadataViewModel":{"title":{"content":"Current video"}}},
                "onTap":{"innertubeCommand":{"watchEndpoint":{"videoId":"Video000004"}}},
                "index":4
              }},
              {"continuationItemViewModel":{"trigger":{"continuationCommand":{
                "token":"CurrentContinuation_1"
              }}}}
            ]};
            </script>
            """);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.Equal(1, result.Value.Items.Count);
        Assert.Equal("Video000004", result.Value.Items[0].VideoId.Value);
        Assert.Equal("Current video", result.Value.Items[0].Title);
        Assert.Equal(4, result.Value.Items[0].Index);
        Assert.Equal(TimeSpan.FromSeconds(65), result.Value.Items[0].Duration);
        Assert.Equal("CurrentContinuation_1", result.Value.ContinuationToken);
    }
}

internal static class CollectionFixtures
{
    public const string InitialHtml = """
        <script>
        ytcfg.set({
          "INNERTUBE_API_KEY":"FixtureKey_123",
          "INNERTUBE_CLIENT_VERSION":"2.20260716.00.00",
          "VISITOR_DATA":"VisitorFixture_1"
        });
        var ytInitialData = {
          "metadata":{"playlistMetadataRenderer":{"title":"Fixture playlist"}},
          "contents":[
            {"playlistVideoRenderer":{
              "videoId":"Video000001",
              "title":{"simpleText":"First video"},
              "index":{"simpleText":"1"},
              "lengthSeconds":"61",
              "thumbnail":{"thumbnails":[
                {"url":"https://i.ytimg.com/vi/Video000001/default.jpg"},
                {"url":"https://i.ytimg.com/vi/Video000001/hqdefault.jpg"}
              ]}
            }},
            {"continuationItemRenderer":{"continuationEndpoint":{"continuationCommand":{
              "token":"ContinuationFixture_1"
            }}}}
          ]
        };
        </script>
        """;

    public const string ContinuationJson = """
        {
          "onResponseReceivedActions":[{"appendContinuationItemsAction":{"continuationItems":[
            {"playlistVideoRenderer":{
              "videoId":"Video000002",
              "title":{"runs":[{"text":"Second "},{"text":"video"}]},
              "index":{"simpleText":"2"},
              "lengthSeconds":"120"
            }},
            {"playlistVideoRenderer":{
              "videoId":"Video000001",
              "title":{"simpleText":"Duplicate first"},
              "index":{"simpleText":"1"}
            }}
          ]}}]
        }
        """;
}
