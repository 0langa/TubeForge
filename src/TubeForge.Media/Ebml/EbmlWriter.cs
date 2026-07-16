namespace TubeForge.Media.Ebml;

internal static class EbmlWriter
{
    public static byte[] Element(uint id, params byte[][] payloadParts)
    {
        var payloadLength = payloadParts.Aggregate(0L, (sum, part) => checked(sum + part.Length));
        if (payloadLength > int.MaxValue)
        {
            throw new OverflowException("An in-memory EBML element exceeds the supported size.");
        }

        var idBytes = EncodeId(id);
        var sizeBytes = EncodeSize((ulong)payloadLength);
        var output = new byte[checked(idBytes.Length + sizeBytes.Length + (int)payloadLength)];
        idBytes.CopyTo(output, 0);
        sizeBytes.CopyTo(output, idBytes.Length);
        var offset = idBytes.Length + sizeBytes.Length;
        foreach (var part in payloadParts)
        {
            part.CopyTo(output, offset);
            offset += part.Length;
        }

        return output;
    }

    public static byte[] UnknownSizeSegmentHeader() =>
        [.. EncodeId(EbmlReader.SegmentId), 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    public static byte[] UnsignedElement(uint id, ulong value) => Element(id, EncodeUnsigned(value));

    public static byte[] EncodeUnsigned(ulong value)
    {
        var width = 1;
        while (width < 8 && value >= (1UL << (width * 8)))
        {
            width++;
        }

        var bytes = new byte[width];
        WriteUnsigned(bytes, value);
        return bytes;
    }

    public static void WriteUnsigned(Span<byte> destination, ulong value)
    {
        if (destination.Length is < 1 or > 8 ||
            (destination.Length < 8 && value >= (1UL << (destination.Length * 8))))
        {
            throw new OverflowException("An EBML unsigned integer does not fit its destination width.");
        }

        for (var index = destination.Length - 1; index >= 0; index--)
        {
            destination[index] = (byte)value;
            value >>= 8;
        }
    }

    public static byte[] EncodeVintValue(ulong value, int width)
    {
        if (width is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        var maximum = (1UL << (7 * width)) - 2;
        if (value > maximum)
        {
            throw new OverflowException("An EBML VINT value does not fit its destination width.");
        }

        var bytes = new byte[width];
        var remaining = value;
        for (var index = width - 1; index >= 0; index--)
        {
            bytes[index] = (byte)remaining;
            remaining >>= 8;
        }

        bytes[0] |= (byte)(1 << (8 - width));
        return bytes;
    }

    private static byte[] EncodeSize(ulong value)
    {
        for (var width = 1; width <= 8; width++)
        {
            var maximum = (1UL << (7 * width)) - 2;
            if (value <= maximum)
            {
                return EncodeVintValue(value, width);
            }
        }

        throw new OverflowException("An EBML element size exceeds the supported VINT range.");
    }

    private static byte[] EncodeId(uint id)
    {
        var width = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
        var bytes = new byte[width];
        for (var index = width - 1; index >= 0; index--)
        {
            bytes[index] = (byte)id;
            id >>= 8;
        }

        if (EbmlReader.VintWidth(bytes[0], 4) != width)
        {
            throw new ArgumentException("The value is not a valid EBML element ID.", nameof(id));
        }

        return bytes;
    }
}
