using System.Runtime.InteropServices;
using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;

namespace TubeForge.Transcoding;

public sealed class WindowsMediaFoundationTranscoder
{
    public Task<Result<AudioTranscodeReceipt>> TranscodeAsync(
        AudioTranscodeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Transcode(request, cancellationToken), CancellationToken.None);
    }

    private static Result<AudioTranscodeReceipt> Transcode(
        AudioTranscodeRequest request,
        CancellationToken cancellationToken)
    {
        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return Result<AudioTranscodeReceipt>.Failure(validation);
        }

        var destination = Path.GetFullPath(request.DestinationPath);
        var temporary = destination + ".transcoding.mp3";
        IMFSourceReader? reader = null;
        IMFSinkWriter? writer = null;
        IMFMediaType? nativeType = null;
        IMFMediaType? pcmType = null;
        IMFMediaType? mp3Type = null;
        var comInitialized = false;
        var mediaFoundationStarted = false;
        try
        {
            Trace("start");
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(temporary);

            var initializeResult = MediaFoundationNative.CoInitializeEx(
                IntPtr.Zero,
                MediaFoundationNative.CoinitMultithreaded);
            if (initializeResult < 0 && initializeResult != MediaFoundationNative.RpcEChangedMode)
            {
                Marshal.ThrowExceptionForHR(initializeResult);
            }

            comInitialized = initializeResult >= 0;
            Check(MediaFoundationNative.MFStartup(MediaFoundationNative.MfVersion));
            mediaFoundationStarted = true;
            Trace("mf-started");
            Check(MediaFoundationNative.MFCreateSourceReaderFromURL(
                Path.GetFullPath(request.SourcePath),
                null,
                out reader));
            Trace("reader-created");
            Check(reader.SetStreamSelection(MediaFoundationNative.SourceReaderAllStreams, 0));
            Check(reader.SetStreamSelection(MediaFoundationNative.SourceReaderFirstAudioStream, 1));
            Check(reader.GetNativeMediaType(
                MediaFoundationNative.SourceReaderFirstAudioStream,
                0,
                out nativeType));
            Trace("native-type-read");

            var nativeAttributes = (IMFAttributes)nativeType;
            var channels = GetUInt32(nativeAttributes, MediaFoundationNative.MfMtAudioChannels);
            var sampleRate = GetUInt32(nativeAttributes, MediaFoundationNative.MfMtAudioSamplesPerSecond);
            if (channels is < 1 or > 2 || sampleRate is not (32_000 or 44_100 or 48_000))
            {
                return Unsupported($"channels={channels};sampleRate={sampleRate}");
            }

            Check(MediaFoundationNative.MFCreateMediaType(out pcmType));
            ConfigurePcm((IMFAttributes)pcmType, channels, sampleRate);
            Check(reader.SetCurrentMediaType(
                MediaFoundationNative.SourceReaderFirstAudioStream,
                IntPtr.Zero,
                pcmType));
            Trace("pcm-configured");

            Check(MediaFoundationNative.MFCreateMediaType(out mp3Type));
            ConfigureMp3((IMFAttributes)mp3Type, channels, sampleRate, request.Output.BitrateKbps);
            Check(MediaFoundationNative.MFCreateSinkWriterFromURL(temporary, IntPtr.Zero, null, out writer));
            Trace("writer-created");
            Check(writer.AddStream(mp3Type, out var outputStream));
            Check(writer.SetInputMediaType(outputStream, pcmType, null));
            Check(writer.BeginWriting());
            Trace("writer-started");

            var sampleCount = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Check(reader.ReadSample(
                    MediaFoundationNative.SourceReaderFirstAudioStream,
                    0,
                    out _,
                    out var flags,
                    out var timestamp,
                    out var sample));
                try
                {
                    if ((flags & MediaFoundationNative.SourceReaderCurrentMediaTypeChanged) != 0)
                    {
                        return Unsupported("source media type changed during conversion");
                    }

                    if ((flags & MediaFoundationNative.SourceReaderStreamTick) != 0)
                    {
                        Check(writer.SendStreamTick(outputStream, timestamp));
                    }

                    if (sample is not null)
                    {
                        Check(writer.WriteSample(outputStream, sample));
                        sampleCount++;
                    }

                    if ((flags & MediaFoundationNative.SourceReaderEndOfStream) != 0)
                    {
                        break;
                    }
                }
                finally
                {
                    Release(sample);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            Trace($"samples-written:{sampleCount}");
            Check(writer.FinalizeWriting());
            Trace("writer-finalized");
            Release(writer);
            writer = null;
            var outputValidation = Mp3FileValidator.Validate(temporary);
            if (!outputValidation.IsSuccess)
            {
                return Result<AudioTranscodeReceipt>.Failure(outputValidation.Error!);
            }

            File.Move(temporary, destination, overwrite: false);
            Trace("published");
            return Result<AudioTranscodeReceipt>.Success(new AudioTranscodeReceipt(
                destination,
                new FileInfo(destination).Length,
                request.Output.BitrateKbps,
                checked((int)channels),
                checked((int)sampleRate)));
        }
        catch (OperationCanceledException)
        {
            return Result<AudioTranscodeReceipt>.Failure(new TubeForgeError(
                "Operation.Cancelled",
                "The audio conversion was cancelled."));
        }
        catch (COMException exception)
        {
            return Unsupported($"HRESULT=0x{exception.HResult:x8}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Result<AudioTranscodeReceipt>.Failure(new TubeForgeError(
                "Media.TranscodeWriteFailed",
                "TubeForge could not write the converted audio file.",
                exception.GetType().Name));
        }
        finally
        {
            Release(writer);
            Release(mp3Type);
            Release(pcmType);
            Release(nativeType);
            Release(reader);
            if (mediaFoundationStarted)
            {
                _ = MediaFoundationNative.MFShutdown();
            }

            if (comInitialized)
            {
                MediaFoundationNative.CoUninitialize();
            }

            TryDelete(temporary);
        }
    }

    private static TubeForgeError? ValidateRequest(AudioTranscodeRequest request)
    {
        if (!request.Output.IsValid || request.Output.Kind != AudioOutputKind.Mp3)
        {
            return new TubeForgeError("Media.InvalidTranscodeProfile", "Select a supported MP3 output profile.");
        }

        if (string.IsNullOrWhiteSpace(request.SourcePath) ||
            string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return new TubeForgeError("Media.InvalidTranscodePath", "Select valid source and destination paths.");
        }

        try
        {
            var source = Path.GetFullPath(request.SourcePath);
            var destination = Path.GetFullPath(request.DestinationPath);
            if (!File.Exists(source) || File.Exists(destination) ||
                source.Equals(destination, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(destination), ".mp3", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(Path.GetDirectoryName(destination)))
            {
                return new TubeForgeError("Media.InvalidTranscodePath", "Select valid source and destination paths.");
            }
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new TubeForgeError(
                "Media.InvalidTranscodePath",
                "Select valid source and destination paths.",
                exception.GetType().Name);
        }

        return null;
    }

    private static void ConfigurePcm(IMFAttributes attributes, uint channels, uint sampleRate)
    {
        SetGuid(attributes, MediaFoundationNative.MfMtMajorType, MediaFoundationNative.MfMediaTypeAudio);
        SetGuid(attributes, MediaFoundationNative.MfMtSubtype, MediaFoundationNative.MfAudioFormatPcm);
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioChannels, channels);
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioSamplesPerSecond, sampleRate);
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioBitsPerSample, 16);
        var blockAlignment = checked(channels * 2);
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioBlockAlignment, blockAlignment);
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioAverageBytesPerSecond, checked(sampleRate * blockAlignment));
    }

    private static void ConfigureMp3(
        IMFAttributes attributes,
        uint channels,
        uint sampleRate,
        int bitrateKbps)
    {
        SetGuid(attributes, MediaFoundationNative.MfMtMajorType, MediaFoundationNative.MfMediaTypeAudio);
        SetGuid(attributes, MediaFoundationNative.MfMtSubtype, MediaFoundationNative.MfAudioFormatMp3);
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioChannels, channels);
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioSamplesPerSecond, sampleRate);
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioAverageBytesPerSecond, checked((uint)bitrateKbps * 1000 / 8));
        SetUInt32(attributes, MediaFoundationNative.MfMtAudioBlockAlignment, 1);
    }

    private static uint GetUInt32(IMFAttributes attributes, Guid key)
    {
        Check(attributes.GetUINT32(ref key, out var value));
        return value;
    }

    private static void SetUInt32(IMFAttributes attributes, Guid key, uint value) =>
        Check(attributes.SetUINT32(ref key, value));

    private static void SetGuid(IMFAttributes attributes, Guid key, Guid value) =>
        Check(attributes.SetGUID(ref key, ref value));

    private static void Check(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }

    private static Result<AudioTranscodeReceipt> Unsupported(string detail) =>
        Result<AudioTranscodeReceipt>.Failure(new TubeForgeError(
            "Media.TranscodeUnsupported",
            "Windows Media Foundation could not decode this source or configure the selected MP3 output.",
            detail));

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            _ = Marshal.FinalReleaseComObject(value);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void Trace(string step)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("TUBEFORGE_TRANSCODE_TRACE"),
                "1",
                StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"TubeForge.Transcoding: {step}");
        }
    }
}
