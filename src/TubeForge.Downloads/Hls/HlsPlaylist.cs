namespace TubeForge.Downloads.Hls;

public sealed record HlsVariant(Uri Uri, long Bandwidth);

public sealed record HlsSegment(
    long Sequence,
    Uri Uri,
    TimeSpan Duration,
    Uri? InitializationUri);

public sealed record HlsPlaylist(
    bool IsMaster,
    bool IsEndList,
    TimeSpan TargetDuration,
    IReadOnlyList<HlsVariant> Variants,
    IReadOnlyList<HlsSegment> Segments);
