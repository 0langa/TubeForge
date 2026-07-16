using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Transcoding;

public static class Mp3FileValidator
{
    private const int MaximumProbeBytes = 64 * 1024;

    public static Result<bool> Validate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 4)
            {
                return Invalid();
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var probe = new byte[Math.Min(MaximumProbeBytes, checked((int)Math.Min(info.Length, int.MaxValue)))];
            stream.ReadExactly(probe);
            var offset = Id3PayloadOffset(probe);
            for (var index = offset; index + 3 < probe.Length; index++)
            {
                if (probe[index] == 0xff &&
                    (probe[index + 1] & 0xe0) == 0xe0 &&
                    (probe[index + 1] & 0x18) != 0x08 &&
                    (probe[index + 2] & 0xf0) != 0xf0 &&
                    (probe[index + 2] & 0x0c) != 0x0c)
                {
                    return Result<bool>.Success(true);
                }
            }

            return Invalid();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<bool>.Failure(new TubeForgeError(
                "Media.TranscodeValidationFailed",
                "TubeForge could not validate the converted MP3 file.",
                exception.GetType().Name));
        }
    }

    private static int Id3PayloadOffset(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || bytes[0] != (byte)'I' || bytes[1] != (byte)'D' || bytes[2] != (byte)'3')
        {
            return 0;
        }

        if ((bytes[6] | bytes[7] | bytes[8] | bytes[9]) >= 0x80)
        {
            return 0;
        }

        var length = (bytes[6] << 21) | (bytes[7] << 14) | (bytes[8] << 7) | bytes[9];
        return Math.Min(bytes.Length, checked(10 + length));
    }

    private static Result<bool> Invalid() => Result<bool>.Failure(new TubeForgeError(
        "Media.InvalidTranscodeOutput",
        "Windows produced an invalid or empty MP3 file."));
}
