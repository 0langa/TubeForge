using TubeForge.Downloads.Hls;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Downloads;

public static class HlsPlaylistParserTests
{
    private static readonly Uri Origin = new("https://manifest.googlevideo.com/live/master.m3u8");

    [Test]
    public static void ParsesMasterAndOrdersVariantsByBandwidth()
    {
        var result = HlsPlaylistParser.Parse("""
            #EXTM3U
            #EXT-X-STREAM-INF:BANDWIDTH=800000,RESOLUTION=640x360
            low/index.m3u8
            #EXT-X-STREAM-INF:BANDWIDTH=2400000,RESOLUTION=1280x720
            /live/high/index.m3u8
            """, Origin);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.True(result.Value.IsMaster);
        Assert.Equal(2, result.Value.Variants.Count);
        Assert.Equal(2_400_000L, result.Value.Variants[0].Bandwidth);
        Assert.Equal("https://manifest.googlevideo.com/live/high/index.m3u8", result.Value.Variants[0].Uri.AbsoluteUri);
    }

    [Test]
    public static void ParsesMediaSequenceMapDiscontinuityAndEndList()
    {
        var result = HlsPlaylistParser.Parse("""
            #EXTM3U
            #EXT-X-TARGETDURATION:6
            #EXT-X-MEDIA-SEQUENCE:42
            #EXT-X-KEY:METHOD=NONE
            #EXT-X-MAP:URI="init.mp4"
            #EXTINF:5.5,
            segment-42.m4s
            #EXT-X-DISCONTINUITY
            #EXTINF:6,
            segment-43.m4s
            #EXT-X-ENDLIST
            """, Origin);

        Assert.True(result.IsSuccess, result.Error?.TechnicalDetail);
        Assert.False(result.Value.IsMaster);
        Assert.True(result.Value.IsEndList);
        Assert.Equal(42L, result.Value.Segments[0].Sequence);
        Assert.Equal(TimeSpan.FromMilliseconds(5_500), result.Value.Segments[0].Duration);
        Assert.Equal("https://manifest.googlevideo.com/live/init.mp4", result.Value.Segments[0].InitializationUri?.AbsoluteUri);
    }

    [Test]
    public static void RejectsEncryptionByteRangesAndMalformedOrOversizedPlaylists()
    {
        var encrypted = HlsPlaylistParser.Parse("""
            #EXTM3U
            #EXT-X-KEY:METHOD=AES-128,URI="key.bin"
            #EXTINF:6,
            segment.ts
            """, Origin);
        Assert.False(encrypted.IsSuccess);
        Assert.Equal("Hls.EncryptedUnsupported", encrypted.Error?.Code);

        var ranged = HlsPlaylistParser.Parse("""
            #EXTM3U
            #EXTINF:6,
            #EXT-X-BYTERANGE:1024@0
            segment.ts
            """, Origin);
        Assert.Equal("Hls.UnsupportedPlaylist", ranged.Error?.Code);

        Assert.False(HlsPlaylistParser.Parse("not hls", Origin).IsSuccess);
        Assert.False(HlsPlaylistParser.Parse(new string('x', HlsPlaylistParser.MaximumCharacters + 1), Origin).IsSuccess);
    }
}
