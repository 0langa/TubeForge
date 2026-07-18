using System.Buffers.Binary;
using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;

namespace TubeForge.Media;

public static class MediaContainerValidator
{
    private const int HeaderProbeBytes = 32;

    public static Result<bool> Validate(string path, MediaContainer expectedContainer)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Invalid("The media path is empty.");
        }

        try
        {
            using var stream = new FileStream(
                Path.GetFullPath(path),
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: HeaderProbeBytes,
                FileOptions.SequentialScan);
            Span<byte> header = stackalloc byte[HeaderProbeBytes];
            var bytesRead = stream.Read(header);
            var valid = expectedContainer switch
            {
                MediaContainer.Mp4 or MediaContainer.ThreeGp =>
                    IsValidIsoBaseMediaHeader(header[..bytesRead], stream.Length),
                MediaContainer.WebM or MediaContainer.Mkv =>
                    IsValidEbmlHeader(header[..bytesRead], stream.Length),
                _ => false
            };
            return valid
                ? Result<bool>.Success(true)
                : Invalid($"The file does not contain a valid {expectedContainer} header.");
        }
        catch (FileNotFoundException exception)
        {
            return Invalid("The downloaded media file is missing.", exception.GetType().Name);
        }
        catch (DirectoryNotFoundException exception)
        {
            return Invalid("The downloaded media directory is missing.", exception.GetType().Name);
        }
        catch (UnauthorizedAccessException exception)
        {
            return Invalid("TubeForge cannot inspect the downloaded media file.", exception.GetType().Name);
        }
        catch (IOException exception)
        {
            return Invalid("TubeForge cannot inspect the downloaded media file.", exception.GetType().Name);
        }
        catch (ArgumentException exception)
        {
            return Invalid("The downloaded media path is invalid.", exception.GetType().Name);
        }
        catch (NotSupportedException exception)
        {
            return Invalid("The downloaded media path is unsupported.", exception.GetType().Name);
        }
    }

    private static bool IsValidIsoBaseMediaHeader(ReadOnlySpan<byte> header, long fileLength)
    {
        if (header.Length < 16 || fileLength < 16 ||
            !header.Slice(4, 4).SequenceEqual("ftyp"u8))
        {
            return false;
        }

        var shortSize = BinaryPrimitives.ReadUInt32BigEndian(header);
        ulong boxSize;
        if (shortSize == 1)
        {
            boxSize = BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
            if (boxSize < 16)
            {
                return false;
            }
        }
        else
        {
            boxSize = shortSize;
            if (boxSize < 16)
            {
                return false;
            }
        }

        return boxSize <= (ulong)fileLength;
    }

    private static bool IsValidEbmlHeader(ReadOnlySpan<byte> header, long fileLength)
    {
        ReadOnlySpan<byte> ebmlMagic = [0x1A, 0x45, 0xDF, 0xA3];
        if (header.Length < 5 || fileLength < 5 || !header[..4].SequenceEqual(ebmlMagic))
        {
            return false;
        }

        var firstSizeByte = header[4];
        var marker = 0x80;
        var width = 1;
        while (width <= 8 && (firstSizeByte & marker) == 0)
        {
            marker >>= 1;
            width++;
        }

        if (width > 8 || header.Length < 4 + width)
        {
            return false;
        }

        ulong value = (ulong)(firstSizeByte & (marker - 1));
        for (var index = 1; index < width; index++)
        {
            value = checked((value << 8) | header[4 + index]);
        }

        var unknownValue = (1UL << (7 * width)) - 1;
        return value != unknownValue && value <= (ulong)fileLength - (ulong)(4 + width);
    }

    private static Result<bool> Invalid(string detail, string? technicalDetail = null) =>
        Result<bool>.Failure(new TubeForgeError(
            "Media.InvalidStructure",
            "The downloaded file does not match its declared media container.",
            technicalDetail is null ? detail : $"{detail} {technicalDetail}"));
}
