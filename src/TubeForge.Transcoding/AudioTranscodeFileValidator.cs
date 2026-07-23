using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;

namespace TubeForge.Transcoding;

public static class AudioTranscodeFileValidator
{
    public static Result<bool> Validate(string path, OutputProfileKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (kind == OutputProfileKind.Mp3)
        {
            return Mp3FileValidator.Validate(path);
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 4)
            {
                return Invalid();
            }

            Span<byte> header = stackalloc byte[12];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var count = stream.Read(header);
            var valid = kind switch
            {
                OutputProfileKind.Aac => count >= 8 && header[4..8].SequenceEqual("ftyp"u8),
                OutputProfileKind.Opus => count >= 4 && header[..4].SequenceEqual("OggS"u8),
                OutputProfileKind.Wav => count >= 12 &&
                                       header[..4].SequenceEqual("RIFF"u8) &&
                                       header[8..12].SequenceEqual("WAVE"u8),
                OutputProfileKind.Flac => count >= 4 && header[..4].SequenceEqual("fLaC"u8),
                _ => false
            };
            return valid ? Result<bool>.Success(true) : Invalid();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<bool>.Failure(new TubeForgeError(
                "Media.TranscodeValidationFailed",
                "TubeForge could not validate the converted audio file.",
                exception.GetType().Name));
        }
    }

    private static Result<bool> Invalid() => Result<bool>.Failure(new TubeForgeError(
        "Media.InvalidTranscodeOutput",
        "FFmpeg produced an invalid or empty converted audio file."));
}
