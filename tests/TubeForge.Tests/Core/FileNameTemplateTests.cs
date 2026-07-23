using TubeForge.Core.Files;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class FileNameTemplateTests
{
    [Test]
    public static void RendersSupportedTokensIndexesAndEscapedBraces()
    {
        var context = new FileNameTemplateContext
        {
            Title = "Fixture title",
            Channel = "Fixture channel",
            VideoId = "Video000001",
            Quality = "2160p",
            Container = "mp4",
            Index = 7,
            ChapterIndex = 2,
            ChapterTitle = "Main section",
            IndexWidth = 3
        };

        var result = FileNameTemplate.Render(
            "{index} - {title} [{quality} {container}] by {channel} {{id {videoId}}} - {chapterIndex} {chapterTitle}",
            context);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.Equal(
            "007 - Fixture title [2160p mp4] by Fixture channel {id Video000001} - 002 Main section",
            result.Value);
    }

    [Test]
    public static void OmitsUnavailableIndexAndRejectsMalformedOrUnknownTokens()
    {
        var context = new FileNameTemplateContext
        {
            Title = "Fixture",
            Channel = "Channel",
            VideoId = "Video000001",
            Quality = "audio",
            Container = "m4a"
        };

        var noIndex = FileNameTemplate.Render("{index}{title}", context);
        Assert.True(noIndex.IsSuccess, noIndex.Error?.TechnicalDetail);
        Assert.Equal("Fixture", noIndex.Value);

        foreach (var template in new[] { "{unknown}", "{title", "title}", "{title}}", "   " })
        {
            Assert.False(FileNameTemplate.Render(template, context).IsSuccess, template);
        }
    }
}
