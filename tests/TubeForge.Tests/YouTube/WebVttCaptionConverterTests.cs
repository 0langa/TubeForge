using TubeForge.Tests.Framework;
using TubeForge.YouTube.Captions;

namespace TubeForge.Tests.YouTube;

public static class WebVttCaptionConverterTests
{
    private const string Fixture = """
        WEBVTT
        Kind: captions
        Language: en

        cue-1
        00:00:01.250 --> 00:00:03.500 align:start position:0%
        <v Alice>Hello &amp; <b>world</b></v>

        00:04.000 --> 00:05.125
        Second line
        """;

    [Test]
    public static void NormalizesWebVttAndConvertsSafeSubRip()
    {
        var normalized = WebVttCaptionConverter.Normalize(Fixture);
        var subRip = WebVttCaptionConverter.ConvertToSubRip(Fixture);

        Assert.True(normalized.IsSuccess, normalized.Error?.Message);
        Assert.True(normalized.Value.StartsWith("WEBVTT\n\n00:00:01.250 --> 00:00:03.500", StringComparison.Ordinal));
        Assert.True(subRip.IsSuccess, subRip.Error?.Message);
        Assert.True(subRip.Value.Contains("1\n00:00:01,250 --> 00:00:03,500", StringComparison.Ordinal));
        Assert.True(subRip.Value.Contains("Hello & <b>world</b>", StringComparison.Ordinal));
        Assert.False(subRip.Value.Contains("<v", StringComparison.Ordinal));
        Assert.Equal(2, WebVttCaptionConverter.CountCues(Fixture).Value);
    }

    [Test]
    public static void RejectsMalformedOrReversedCues()
    {
        var wrongHeader = WebVttCaptionConverter.Normalize("not-vtt");
        var reversed = WebVttCaptionConverter.ConvertToSubRip("""
            WEBVTT

            00:02.000 --> 00:01.000
            invalid
            """);

        Assert.False(wrongHeader.IsSuccess);
        Assert.Equal("Caption.InvalidWebVtt", wrongHeader.Error?.Code);
        Assert.False(reversed.IsSuccess);
        Assert.Equal("Caption.InvalidWebVtt", reversed.Error?.Code);
    }

    [Test]
    public static void AcceptsYouTubeAutoCaptionWhitespacePayloadLines()
    {
        const string autoCaption =
            "WEBVTT\nKind: captions\nLanguage: en\n\n" +
            "00:00:00.240 --> 00:00:02.149 align:start position:0%\n" +
            " \n" +
            "Last<00:00:00.480><c> week,</c><00:00:00.880><c> Bun</c>\n\n";

        var subRip = WebVttCaptionConverter.ConvertToSubRip(autoCaption);

        Assert.True(subRip.IsSuccess, subRip.Error?.Message);
        Assert.True(subRip.Value.Contains("Last week, Bun", StringComparison.Ordinal));
        Assert.Equal(1, WebVttCaptionConverter.CountCues(autoCaption).Value);
    }
}
