using System.Buffers.Binary;
using System.Text;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Media.IsoBmff;

public static class IsoBmffReader
{
    private const int MaximumTopLevelBoxes = 1_000_000;

    public static Result<IReadOnlyList<IsoBmffBoxHeader>> ReadTopLevel(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Invalid("The ISO BMFF path is empty.");
        }

        try
        {
            using var stream = new FileStream(
                Path.GetFullPath(path),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.RandomAccess);
            if (stream.Length < 8)
            {
                return Invalid("The file is shorter than one ISO BMFF box header.");
            }

            var boxes = new List<IsoBmffBoxHeader>();
            var offset = 0L;
            Span<byte> baseHeader = stackalloc byte[8];
            Span<byte> extendedSize = stackalloc byte[8];
            while (offset < stream.Length)
            {
                if (boxes.Count >= MaximumTopLevelBoxes || stream.Length - offset < 8)
                {
                    return Invalid("The top-level ISO BMFF box table is truncated or excessive.");
                }

                stream.Position = offset;
                stream.ReadExactly(baseHeader);
                var shortSize = BinaryPrimitives.ReadUInt32BigEndian(baseHeader);
                var type = Encoding.ASCII.GetString(baseHeader[4..]);
                var headerSize = 8;
                ulong declaredSize = shortSize;
                if (shortSize == 1)
                {
                    if (stream.Length - offset < 16)
                    {
                        return Invalid("An extended ISO BMFF box header is truncated.");
                    }

                    stream.ReadExactly(extendedSize);
                    declaredSize = BinaryPrimitives.ReadUInt64BigEndian(extendedSize);
                    headerSize = 16;
                }

                if (type == "uuid")
                {
                    headerSize += 16;
                }

                var size = shortSize == 0
                    ? (ulong)(stream.Length - offset)
                    : declaredSize;
                if (size < (ulong)headerSize || size > long.MaxValue ||
                    size > (ulong)(stream.Length - offset))
                {
                    return Invalid($"Box '{type}' has an invalid or out-of-bounds size.");
                }

                var signedSize = (long)size;
                boxes.Add(new IsoBmffBoxHeader(type, offset, signedSize, headerSize));
                offset = checked(offset + signedSize);
            }

            return boxes.Count == 0
                ? Invalid("The file contains no ISO BMFF boxes.")
                : Result<IReadOnlyList<IsoBmffBoxHeader>>.Success(boxes);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            return Invalid("TubeForge could not safely read the ISO BMFF box table.", exception.GetType().Name);
        }
    }

    private static Result<IReadOnlyList<IsoBmffBoxHeader>> Invalid(
        string detail,
        string? technicalDetail = null) =>
        Result<IReadOnlyList<IsoBmffBoxHeader>>.Failure(new TubeForgeError(
            "Media.InvalidIsoBmff",
            "The MP4 file contains an invalid ISO BMFF structure.",
            technicalDetail is null ? detail : $"{detail} {technicalDetail}"));
}
