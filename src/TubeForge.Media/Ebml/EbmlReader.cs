using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Media.Ebml;

public static class EbmlReader
{
    public const uint HeaderId = 0x1A45DFA3;
    public const uint SegmentId = 0x18538067;
    private const int MaximumElements = 1_000_000;

    public static Result<EbmlDocument> ReadDocument(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Invalid<EbmlDocument>("The WebM path is empty.");
        }

        try
        {
            using var stream = Open(path);
            var header = ReadElementHeader(stream, 0, stream.Length);
            if (header.Id != HeaderId || header.IsUnknownSize)
            {
                return Invalid<EbmlDocument>("The file does not start with a bounded EBML header.");
            }

            var segment = ReadElementHeader(stream, header.EndOffset, stream.Length);
            if (segment.Id != SegmentId || segment.EndOffset != stream.Length)
            {
                return Invalid<EbmlDocument>("The EBML header is not followed by one complete Segment.");
            }

            var children = ReadChildren(stream, segment);
            return Result<EbmlDocument>.Success(new EbmlDocument(header, segment, children));
        }
        catch (EbmlFormatException exception)
        {
            return Invalid<EbmlDocument>(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException or OverflowException)
        {
            return Invalid<EbmlDocument>("TubeForge could not safely read the EBML document.", exception.GetType().Name);
        }
    }

    public static Result<IReadOnlyList<EbmlElementHeader>> ReadChildren(
        string path,
        EbmlElementHeader parent)
    {
        try
        {
            using var stream = Open(path);
            if (parent.Offset < 0 || parent.EndOffset > stream.Length)
            {
                return Invalid<IReadOnlyList<EbmlElementHeader>>("The requested EBML parent is outside the file.");
            }

            return Result<IReadOnlyList<EbmlElementHeader>>.Success(ReadChildren(stream, parent));
        }
        catch (EbmlFormatException exception)
        {
            return Invalid<IReadOnlyList<EbmlElementHeader>>(exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException or OverflowException)
        {
            return Invalid<IReadOnlyList<EbmlElementHeader>>(
                "TubeForge could not safely read the EBML children.",
                exception.GetType().Name);
        }
    }

    internal static IReadOnlyList<EbmlElementHeader> ReadChildren(
        FileStream stream,
        EbmlElementHeader parent) =>
        ReadElements(stream, parent.DataOffset, parent.EndOffset);

    internal static IReadOnlyList<EbmlElementHeader> ReadElements(
        FileStream stream,
        long start,
        long end)
    {
        if (start < 0 || end < start || end > stream.Length)
        {
            throw new EbmlFormatException("An EBML element range is outside the file.");
        }

        var elements = new List<EbmlElementHeader>();
        var offset = start;
        while (offset < end)
        {
            if (elements.Count >= MaximumElements)
            {
                throw new EbmlFormatException("The EBML element count exceeds the safety limit.");
            }

            var element = ReadElementHeader(stream, offset, end);
            elements.Add(element);
            offset = element.EndOffset;
        }

        return elements;
    }

    internal static EbmlElementHeader ReadElementHeader(FileStream stream, long offset, long end)
    {
        if (offset < 0 || end > stream.Length || end - offset < 2)
        {
            throw new EbmlFormatException("An EBML element header is truncated.");
        }

        Span<byte> header = stackalloc byte[12];
        var available = (int)Math.Min(header.Length, end - offset);
        stream.Position = offset;
        stream.ReadExactly(header[..available]);
        var idWidth = VintWidth(header[0], 4);
        if (idWidth == 0 || available <= idWidth)
        {
            throw new EbmlFormatException("An EBML element ID is invalid or truncated.");
        }

        uint id = 0;
        for (var index = 0; index < idWidth; index++)
        {
            id = (id << 8) | header[index];
        }

        var sizeWidth = VintWidth(header[idWidth], 8);
        if (sizeWidth == 0 || idWidth + sizeWidth > available)
        {
            throw new EbmlFormatException("An EBML element size is invalid or truncated.");
        }

        var marker = 1 << (8 - sizeWidth);
        ulong size = (ulong)(header[idWidth] & (marker - 1));
        for (var index = 1; index < sizeWidth; index++)
        {
            size = (size << 8) | header[idWidth + index];
        }

        var unknownValue = (1UL << (7 * sizeWidth)) - 1;
        var isUnknown = size == unknownValue;
        var headerSize = idWidth + sizeWidth;
        var dataOffset = checked(offset + headerSize);
        var dataSize = isUnknown ? checked(end - dataOffset) : checked((long)size);
        if (dataSize < 0 || dataSize > end - dataOffset)
        {
            throw new EbmlFormatException($"EBML element 0x{id:X} exceeds its parent boundary.");
        }

        return new EbmlElementHeader(id, offset, headerSize, dataSize, isUnknown);
    }

    internal static ulong ReadUnsigned(FileStream stream, EbmlElementHeader element)
    {
        if (element.DataSize is < 1 or > 8)
        {
            throw new EbmlFormatException($"EBML integer 0x{element.Id:X} has an invalid width.");
        }

        Span<byte> bytes = stackalloc byte[8];
        stream.Position = element.DataOffset;
        stream.ReadExactly(bytes[..(int)element.DataSize]);
        ulong value = 0;
        foreach (var valueByte in bytes[..(int)element.DataSize])
        {
            value = (value << 8) | valueByte;
        }

        return value;
    }

    internal static byte[] ReadBytes(FileStream stream, EbmlElementHeader element, int maximumBytes)
    {
        if (element.TotalSize > maximumBytes || element.TotalSize > int.MaxValue)
        {
            throw new EbmlFormatException($"EBML element 0x{element.Id:X} exceeds the metadata safety limit.");
        }

        var bytes = new byte[(int)element.TotalSize];
        stream.Position = element.Offset;
        stream.ReadExactly(bytes);
        return bytes;
    }

    internal static FileStream Open(string path) => new(
        Path.GetFullPath(path),
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        FileOptions.RandomAccess);

    internal static int VintWidth(byte firstByte, int maximum)
    {
        for (var width = 1; width <= maximum; width++)
        {
            if ((firstByte & (0x80 >> (width - 1))) != 0)
            {
                return width;
            }
        }

        return 0;
    }

    private static Result<T> Invalid<T>(string detail, string? technicalDetail = null) =>
        Result<T>.Failure(new TubeForgeError(
            "Media.InvalidEbml",
            "The WebM file contains an invalid EBML structure.",
            technicalDetail is null ? detail : $"{detail} {technicalDetail}"));

    internal sealed class EbmlFormatException(string message) : Exception(message);
}
