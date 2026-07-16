using TubeForge.Tests.Framework;
using TubeForge.YouTube.Extraction;
using TubeForge.YouTube.Player;

namespace TubeForge.Tests.YouTube;

public static class SignatureTransformTests
{
    private const string SyntheticPlayerScript = """
        var AB={
          rv:function(a){a.reverse()},
          sl:function(a,b){a.splice(0,b)},
          sw:function(a,b){var c=a[0];a[0]=a[b%a.length];a[b%a.length]=c}
        };
        XY=function(a){a=a.split("");AB.sw(a,2);AB.rv(a);AB.sl(a,1);return a.join("")};
        """;

    [Test]
    public static void ExtractsAndAppliesConstrainedTransformPlan()
    {
        var plans = SignatureTransformExtractor.Extract(SyntheticPlayerScript);

        Assert.Equal(1, plans.Count);
        Assert.Equal("edabc", plans[0].Apply("abcdef"));
        Assert.SequenceEqual(
            new[]
            {
                SignatureOperationKind.Swap,
                SignatureOperationKind.Reverse,
                SignatureOperationKind.RemoveFirst
            },
            plans[0].Operations.Select(operation => operation.Kind));
    }

    [Test]
    public static void ResolvesCipherWithoutExecutingJavaScript()
    {
        var plan = SignatureTransformExtractor.Extract(SyntheticPlayerScript).Single();
        var cipher =
            "url=https%3A%2F%2Ffixture.googlevideo.com%2Fvideoplayback%3Fitag%3D137" +
            "&s=abcdef&sp=sig";

        var uri = SignatureCipherUrl.Resolve(cipher, plan);

        Assert.True(uri is not null);
        Assert.Equal("fixture.googlevideo.com", uri!.Host);
        Assert.True(uri.Query.Contains("sig=edabc", StringComparison.Ordinal));
    }

    [Test]
    public static void ParserCanMaterializeCipheredFormatWithApprovedResolver()
    {
        var html = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "watch-page-basic.html"));
        var plan = SignatureTransformExtractor.Extract(SyntheticPlayerScript).Single();

        var result = YouTubeWatchPageParser.Parse(html, cipher => SignatureCipherUrl.Resolve(cipher, plan));

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.Equal(3, result.Value.Metadata.Formats.Count);
        Assert.Equal(1, result.Value.CipheredFormatCount);
        Assert.True(result.Value.Metadata.Formats.Any(format => format.FormatId == 137));
    }

    [Test]
    public static void RejectsOversizedOrUnrecognizedPlayerScript()
    {
        Assert.Equal(0, SignatureTransformExtractor.Extract("function x(a){return a}").Count);
        Assert.Equal(0, SignatureTransformExtractor.Extract(new string('x', 6 * 1024 * 1024 + 1)).Count);
    }
}
