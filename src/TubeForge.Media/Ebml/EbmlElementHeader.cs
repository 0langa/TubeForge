namespace TubeForge.Media.Ebml;

public readonly record struct EbmlElementHeader(
    uint Id,
    long Offset,
    int HeaderSize,
    long DataSize,
    bool IsUnknownSize)
{
    public long DataOffset => Offset + HeaderSize;

    public long EndOffset => DataOffset + DataSize;

    public long TotalSize => HeaderSize + DataSize;
}

public sealed record EbmlDocument(
    EbmlElementHeader Header,
    EbmlElementHeader Segment,
    IReadOnlyList<EbmlElementHeader> SegmentChildren);
