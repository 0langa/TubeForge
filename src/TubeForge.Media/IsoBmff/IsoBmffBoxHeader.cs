namespace TubeForge.Media.IsoBmff;

public readonly record struct IsoBmffBoxHeader(
    string Type,
    long Offset,
    long Size,
    int HeaderSize)
{
    public long DataOffset => Offset + HeaderSize;

    public long DataSize => Size - HeaderSize;

    public long EndOffset => Offset + Size;
}
