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

    private const string SyntheticEs6PlayerScript = """
        const decoy = "function(a){a=a.split('');a.reverse();return a.join('')}";
        // function(a){a=a.split("");a.reverse();return a.join("")}
        const OP={
          sw(a,b){const c=a[0];a[0]=a[b%a.length];a[b%a.length]=c},
          rv:(a)=>{a.reverse()},
          sl(a,b){a.splice(0,b)}
        };
        const NX=(a)=>{a=a.split('');OP.sl(a,2);OP.sw(a,3);OP.rv(a);return a.join('')};
        """;

    private const string SyntheticThrottlingScript = """
        const OP={rv(a){a.reverse()},sl(a,b){a.splice(0,b)}};
        const SG=(a)=>{a=a.split('');OP.rv(a);return a.join('')};
        const NT=(a)=>{a=a.split('');OP.sl(a,2);OP.rv(a);return a.join('')};
        const TF=[NT];
        let n=url.searchParams.get('n');if(n){n=TF[0](n);url.searchParams.set('n',n)}
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
    public static void TokenizerSkipsCommentsAndTracksEs6Structure()
    {
        Assert.True(JavaScriptTokenizer.TryTokenize(SyntheticEs6PlayerScript, out var tokens));
        Assert.True(tokens.Any(token => token.Is(SyntheticEs6PlayerScript, "=>")));

        var plans = SignatureTransformExtractor.Extract(SyntheticEs6PlayerScript);

        Assert.Equal(1, plans.Count);
        Assert.Equal("NX", plans[0].Name);
        Assert.Equal("hgcedf", plans[0].Apply("abcdefgh"));
    }

    [Test]
    public static void TokenizerRejectsMalformedOrUnboundedInput()
    {
        Assert.False(JavaScriptTokenizer.TryTokenize("const x='unterminated", out _));
        Assert.False(JavaScriptTokenizer.TryTokenize("/* unterminated", out _));
        Assert.False(JavaScriptTokenizer.TryTokenize(
            new string('x', JavaScriptTokenizer.MaximumSourceLength + 1),
            out _));
    }

    [Test]
    public static void ExtractorHandlesDeterministicWhitespaceAndCommentMutations()
    {
        var mutations = new[]
        {
            SyntheticPlayerScript,
            SyntheticPlayerScript.Replace(";", ";/*safe*/", StringComparison.Ordinal),
            SyntheticPlayerScript.Replace("=function", " = function", StringComparison.Ordinal),
            SyntheticPlayerScript.Replace(".split", " /*x*/ . split", StringComparison.Ordinal),
            SyntheticPlayerScript.Replace(".join", "\n.join", StringComparison.Ordinal)
        };

        foreach (var mutation in mutations)
        {
            var plans = SignatureTransformExtractor.Extract(mutation);
            Assert.Equal(1, plans.Count);
            Assert.Equal("edabc", plans[0].Apply("abcdef"));
        }
    }

    [Test]
    public static void CacheKeysPlansByPlayerScriptHashAndEvictsOldEntries()
    {
        var plan = SignatureTransformExtractor.Extract(SyntheticPlayerScript).Single();
        var bundle = new PlayerTransformPlans(plan, null);
        var cache = new PlayerTransformCache(2);

        cache.Store("script-a", bundle);
        cache.Store(new string("script-a".ToCharArray()), bundle);
        cache.Store("script-b", bundle);
        Assert.True(cache.TryGet("script-a", out _));

        cache.Store("script-c", bundle);
        Assert.False(cache.TryGet("script-a", out _));
        Assert.True(cache.TryGet("script-b", out _));
        Assert.True(cache.TryGet("script-c", out _));
        Assert.Equal(PlayerTransformCache.Hash("script-b"), PlayerTransformCache.Hash("script-b"));
        Assert.False(PlayerTransformCache.Hash("script-b") == PlayerTransformCache.Hash("script-c"));
    }

    [Test]
    public static void LocatesAndAppliesEs6ThrottlingTransform()
    {
        var candidates = SignatureTransformExtractor.Extract(SyntheticThrottlingScript);
        var throttling = ThrottlingTransformExtractor.Extract(SyntheticThrottlingScript, candidates);

        Assert.Equal(1, throttling.Count);
        Assert.Equal("NT", throttling[0].Name);
        var source = new Uri("https://fixture.googlevideo.com/videoplayback?n=abcdef&itag=137");
        var resolved = ThrottlingUrl.Resolve(source, throttling[0]);

        Assert.True(resolved is not null);
        Assert.True(resolved!.Query.Contains("n=fedc", StringComparison.Ordinal));
        Assert.True(resolved.Query.Contains("itag=137", StringComparison.Ordinal));
    }

    [Test]
    public static void FuzzesMalformedPlayerFragmentsWithoutExecutionOrExceptions()
    {
        const string alphabet = "abcXYZ019{}[]();,.=><'\"`/*+-_?$\\\r\n";
        var random = new Random(17_071);
        for (var iteration = 0; iteration < 250; iteration++)
        {
            var length = random.Next(0, 2_048);
            var characters = new char[length];
            for (var index = 0; index < characters.Length; index++)
            {
                characters[index] = alphabet[random.Next(alphabet.Length)];
            }

            _ = SignatureTransformExtractor.Extract(new string(characters));
        }
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
