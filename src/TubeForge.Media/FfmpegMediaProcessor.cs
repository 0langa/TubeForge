using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;
using TubeForge.Media.Ebml;
using TubeForge.Media.IsoBmff;

namespace TubeForge.Media;

public sealed record MediaProcessReceipt(string DestinationPath, long BytesWritten);

public sealed class FfmpegMediaProcessor
{
    private const string RelativeBundledPath = "ffmpeg/ffmpeg.exe";
    private readonly string _executablePath;
    private readonly IFfmpegProcessRunner _processRunner;

    public FfmpegMediaProcessor(string executablePath) : this(executablePath, new FfmpegProcessRunner())
    {
    }

    internal FfmpegMediaProcessor(
        string executablePath,
        IFfmpegProcessRunner processRunner)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        _executablePath = Path.GetFullPath(executablePath);
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public string ExecutablePath => _executablePath;

    public bool IsAvailable => File.Exists(_executablePath);

    public static string BundledExecutablePath(string baseDirectory) =>
        Path.GetFullPath(Path.Combine(baseDirectory, RelativeBundledPath));

    public Task<Result<MediaProcessReceipt>> MuxAsync(
        string videoPath,
        string audioPath,
        string destinationPath,
        MediaContainer outputContainer,
        CancellationToken cancellationToken = default,
        bool allowExistingValidatedOutput = false) =>
        ProcessAsync(
            [videoPath, audioPath],
            destinationPath,
            outputContainer,
            arguments =>
            {
                AddInput(arguments, videoPath);
                AddInput(arguments, audioPath);
                arguments.Add("-map");
                arguments.Add("0:v:0");
                arguments.Add("-map");
                arguments.Add("1:a:0");
                AddStreamCopyOutput(arguments, outputContainer);
            },
            cancellationToken,
            allowExistingValidatedOutput);

    public Task<Result<MediaProcessReceipt>> MuxMp4Async(
        string videoPath,
        string audioPath,
        string destinationPath,
        CancellationToken cancellationToken = default,
        bool allowExistingValidatedOutput = false) =>
        MuxAsync(
            videoPath,
            audioPath,
            destinationPath,
            MediaContainer.Mp4,
            cancellationToken,
            allowExistingValidatedOutput);

    public Task<Result<MediaProcessReceipt>> RemuxMp4Async(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default,
        bool allowExistingValidatedOutput = false) =>
        ProcessAsync(
            [sourcePath],
            destinationPath,
            MediaContainer.Mp4,
            arguments =>
            {
                AddInput(arguments, sourcePath);
                arguments.Add("-map");
                arguments.Add("0");
                AddStreamCopyOutput(arguments, MediaContainer.Mp4);
            },
            cancellationToken,
            allowExistingValidatedOutput);

    public Task<Result<MediaProcessReceipt>> EmbedSubtitleAsync(
        string mediaPath,
        string subtitlePath,
        string destinationPath,
        MediaContainer outputContainer,
        CaptionEmbedSelection caption,
        CancellationToken cancellationToken = default,
        bool allowExistingValidatedOutput = false)
    {
        if (!caption.IsValid)
        {
            return Task.FromResult(Failure(
                "Media.InvalidCaptionSelection",
                "Select a valid caption language before embedding subtitles."));
        }

        return ProcessAsync(
            [mediaPath, subtitlePath],
            destinationPath,
            outputContainer,
            arguments =>
            {
                AddInput(arguments, mediaPath);
                AddInput(arguments, subtitlePath);
                arguments.Add("-map");
                arguments.Add("0:v:0");
                arguments.Add("-map");
                arguments.Add("0:a?");
                arguments.Add("-map");
                arguments.Add("1:0");
                arguments.Add("-c:v");
                arguments.Add("copy");
                arguments.Add("-c:a");
                arguments.Add("copy");
                arguments.Add("-c:s");
                arguments.Add(SubtitleCodec(outputContainer));
                arguments.Add("-metadata:s:s:0");
                arguments.Add($"language={caption.LanguageCode}");
                arguments.Add("-disposition:s:0");
                arguments.Add("0");
                arguments.Add("-map_metadata");
                arguments.Add("-1");
                if (outputContainer == MediaContainer.Mp4)
                {
                    arguments.Add("-movflags");
                    arguments.Add("+faststart");
                }
            },
            cancellationToken,
            allowExistingValidatedOutput,
            ValidateSubtitleStreamAsync);
    }

    public async Task<Result<MediaProcessReceipt>> EmbedMetadataAsync(
        string mediaPath,
        string destinationPath,
        MediaContainer outputContainer,
        IReadOnlyList<VideoChapter> chapters,
        TimeSpan duration,
        string? subtitlePath = null,
        CaptionEmbedSelection? caption = null,
        CancellationToken cancellationToken = default,
        bool allowExistingValidatedOutput = false)
    {
        ArgumentNullException.ThrowIfNull(chapters);
        if ((subtitlePath is null) != (caption is null) || caption is { IsValid: false })
        {
            return Failure(
                "Media.InvalidCaptionSelection",
                "Select a valid caption language and subtitle file before embedding subtitles.");
        }

        var chapterMetadata = BuildChapterMetadata(chapters, duration);
        if (!chapterMetadata.IsSuccess)
        {
            return Result<MediaProcessReceipt>.Failure(chapterMetadata.Error!);
        }

        string? chapterPath = null;
        try
        {
            var destinationFullPath = Path.GetFullPath(destinationPath);
            var directory = Path.GetDirectoryName(destinationFullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Failure("Download.InvalidDestination", "The output directory is invalid.");
            }

            Directory.CreateDirectory(directory);
            chapterPath = destinationFullPath + "." + Guid.NewGuid().ToString("N") + ".chapters.ffmetadata";
            await File.WriteAllTextAsync(
                chapterPath,
                chapterMetadata.Value,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken).ConfigureAwait(false);

            var inputs = subtitlePath is null
                ? new[] { mediaPath, chapterPath }
                : new[] { mediaPath, subtitlePath, chapterPath };
            var chapterInput = subtitlePath is null ? 1 : 2;
            return await ProcessAsync(
                inputs,
                destinationPath,
                outputContainer,
                arguments =>
                {
                    AddInput(arguments, mediaPath);
                    if (subtitlePath is not null)
                    {
                        AddInput(arguments, subtitlePath);
                    }

                    AddInput(arguments, chapterPath);
                    arguments.Add("-map");
                    arguments.Add("0:v:0");
                    arguments.Add("-map");
                    arguments.Add("0:a?");
                    if (subtitlePath is not null)
                    {
                        arguments.Add("-map");
                        arguments.Add("1:0");
                    }

                    arguments.Add("-c:v");
                    arguments.Add("copy");
                    arguments.Add("-c:a");
                    arguments.Add("copy");
                    if (caption is { } selectedCaption)
                    {
                        arguments.Add("-c:s");
                        arguments.Add(SubtitleCodec(outputContainer));
                        arguments.Add("-metadata:s:s:0");
                        arguments.Add($"language={selectedCaption.LanguageCode}");
                        arguments.Add("-disposition:s:0");
                        arguments.Add("0");
                    }

                    arguments.Add("-map_metadata");
                    arguments.Add(chapterInput.ToString(CultureInfo.InvariantCulture));
                    arguments.Add("-map_chapters");
                    arguments.Add(chapterInput.ToString(CultureInfo.InvariantCulture));
                    if (outputContainer == MediaContainer.Mp4)
                    {
                        arguments.Add("-movflags");
                        arguments.Add("+faststart");
                    }
                },
                cancellationToken,
                allowExistingValidatedOutput,
                (path, token) => ValidateEmbeddedMetadataAsync(
                    path,
                    caption is not null,
                    chapters.Count,
                    token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Failure("Operation.Cancelled", "Media metadata embedding was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "Media.MetadataWriteFailed",
                "TubeForge could not prepare chapter metadata.",
                exception.GetType().Name);
        }
        finally
        {
            if (chapterPath is not null)
            {
                TryDelete(chapterPath);
            }
        }
    }

    private async Task<Result<MediaProcessReceipt>> ProcessAsync(
        IReadOnlyList<string> inputs,
        string destinationPath,
        MediaContainer outputContainer,
        Action<ICollection<string>> addOperationArguments,
        CancellationToken cancellationToken,
        bool allowExistingValidatedOutput,
        Func<string, CancellationToken, Task<TubeForgeError?>>? extraValidation = null)
    {
        if (outputContainer is not (MediaContainer.Mp4 or MediaContainer.WebM or MediaContainer.Mkv))
        {
            return Failure(
                "Media.UnsupportedContainer",
                "TubeForge can only finalize MP4, WebM, or MKV media through FFmpeg.");
        }

        string? temporaryPath = null;
        try
        {
            if (!IsAvailable)
            {
                return Failure(
                    "Media.FFmpegMissing",
                    "TubeForge's bundled FFmpeg media engine is missing. Reinstall TubeForge.");
            }

            var fullInputs = inputs.Select(Path.GetFullPath).ToArray();
            var destinationFullPath = Path.GetFullPath(destinationPath);
            if (File.Exists(destinationFullPath))
            {
                var existingValidation = ValidateOutput(destinationFullPath, outputContainer);
                if (allowExistingValidatedOutput && existingValidation is null)
                {
                    if (extraValidation is not null)
                    {
                        var extraError = await extraValidation(destinationFullPath, cancellationToken)
                            .ConfigureAwait(false);
                        if (extraError is not null)
                        {
                            return Result<MediaProcessReceipt>.Failure(extraError);
                        }
                    }

                    return Result<MediaProcessReceipt>.Success(new MediaProcessReceipt(
                        destinationFullPath,
                        new FileInfo(destinationFullPath).Length));
                }

                if (allowExistingValidatedOutput && existingValidation is not null)
                {
                    return Result<MediaProcessReceipt>.Failure(existingValidation);
                }

                return Failure(
                    "Download.DestinationExists",
                    "The selected output file already exists.");
            }

            if (fullInputs.Any(path => !File.Exists(path)))
            {
                return Failure("Media.InputMissing", "One or more downloaded media tracks are missing.");
            }

            if (fullInputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fullInputs.Length ||
                fullInputs.Any(path => path.Equals(destinationFullPath, StringComparison.OrdinalIgnoreCase)))
            {
                return Failure(
                    "Media.InvalidProcessPath",
                    "Media input and output paths must be distinct.");
            }

            var directory = Path.GetDirectoryName(destinationFullPath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return Failure("Download.InvalidDestination", "The output directory is invalid.");
            }

            Directory.CreateDirectory(directory);
            temporaryPath = destinationFullPath + "." + Guid.NewGuid().ToString("N") +
                ".processing." + ContainerExtension(outputContainer);
            var arguments = new List<string>
            {
                "-hide_banner",
                "-loglevel",
                "error",
                "-xerror",
                "-nostdin"
            };
            addOperationArguments(arguments);
            arguments.Add("-f");
            arguments.Add(ContainerFormat(outputContainer));
            arguments.Add(temporaryPath);

            var exitCode = await _processRunner.RunAsync(
                    _executablePath,
                    arguments,
                    directory,
                    cancellationToken)
                .ConfigureAwait(false);
            if (exitCode != 0)
            {
                return Failure(
                    "Media.FFmpegFailed",
                    $"FFmpeg could not finalize this {ContainerLabel(outputContainer)} file.",
                    $"FFmpeg exited with code {exitCode}.");
            }

            var validation = ValidateOutput(temporaryPath, outputContainer);
            if (validation is not null)
            {
                return Result<MediaProcessReceipt>.Failure(validation);
            }


            if (extraValidation is not null)
            {
                var extraError = await extraValidation(temporaryPath, cancellationToken)
                    .ConfigureAwait(false);
                if (extraError is not null)
                {
                    return Result<MediaProcessReceipt>.Failure(extraError);
                }
            }

            File.Move(temporaryPath, destinationFullPath, overwrite: false);
            temporaryPath = null;
            return Result<MediaProcessReceipt>.Success(new MediaProcessReceipt(
                destinationFullPath,
                new FileInfo(destinationFullPath).Length));
        }
        catch (OperationCanceledException)
        {
            return Failure(
                "Operation.Cancelled",
                $"{ContainerLabel(outputContainer)} finalization was cancelled.");
        }
        catch (Win32Exception exception)
        {
            return Failure(
                "Media.FFmpegStartFailed",
                "TubeForge could not start FFmpeg.",
                exception.GetType().Name);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Failure(
                "Media.FFmpegWriteFailed",
                $"TubeForge could not finalize the {ContainerLabel(outputContainer)} file.",
                exception.GetType().Name);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return Failure(
                "Media.InvalidProcessPath",
                "A media processing path is invalid.",
                exception.GetType().Name);
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static void AddInput(ICollection<string> arguments, string path)
    {
        arguments.Add("-i");
        arguments.Add(Path.GetFullPath(path));
    }

    private async Task<TubeForgeError?> ValidateSubtitleStreamAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var arguments = new[]
        {
            "-hide_banner",
            "-loglevel", "error",
            "-xerror",
            "-nostdin",
            "-i", Path.GetFullPath(path),
            "-map", "0:s:0",
            "-c", "copy",
            "-f", "null",
            "-"
        };
        var exitCode = await _processRunner.RunAsync(
                _executablePath,
                arguments,
                Path.GetDirectoryName(Path.GetFullPath(path))!,
                cancellationToken)
            .ConfigureAwait(false);
        return exitCode == 0
            ? null
            : new TubeForgeError(
                "Media.SubtitleValidationFailed",
                "FFmpeg did not verify an embedded soft-subtitle stream.",
                $"FFmpeg exited with code {exitCode}.");
    }

    private async Task<TubeForgeError?> ValidateEmbeddedMetadataAsync(
        string path,
        bool expectsSubtitle,
        int expectedChapterCount,
        CancellationToken cancellationToken)
    {
        if (expectsSubtitle)
        {
            var subtitleError = await ValidateSubtitleStreamAsync(path, cancellationToken)
                .ConfigureAwait(false);
            if (subtitleError is not null)
            {
                return subtitleError;
            }
        }

        var probePath = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".probe.ffmetadata";
        try
        {
            var arguments = new[]
            {
                "-hide_banner",
                "-loglevel", "error",
                "-xerror",
                "-nostdin",
                "-i", Path.GetFullPath(path),
                "-f", "ffmetadata",
                probePath
            };
            var exitCode = await _processRunner.RunAsync(
                    _executablePath,
                    arguments,
                    Path.GetDirectoryName(Path.GetFullPath(path))!,
                    cancellationToken)
                .ConfigureAwait(false);
            if (exitCode != 0 || !File.Exists(probePath))
            {
                return ChapterValidationFailure(exitCode);
            }

            var metadata = await File.ReadAllTextAsync(probePath, cancellationToken).ConfigureAwait(false);
            var actualCount = metadata.Split("[CHAPTER]", StringSplitOptions.None).Length - 1;
            return actualCount == expectedChapterCount
                ? null
                : ChapterValidationFailure(exitCode, $"Expected {expectedChapterCount} chapters; found {actualCount}.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return ChapterValidationFailure(-1, exception.GetType().Name);
        }
        finally
        {
            TryDelete(probePath);
        }
    }

    private static Result<string> BuildChapterMetadata(
        IReadOnlyList<VideoChapter> chapters,
        TimeSpan duration)
    {
        if (chapters.Count is < 1 or > 1_000 || duration <= TimeSpan.Zero)
        {
            return Result<string>.Failure(new TubeForgeError(
                "Media.InvalidChapters",
                "Chapter metadata is empty or exceeds its safe limits."));
        }

        var durationMilliseconds = (long)Math.Floor(duration.TotalMilliseconds);
        var builder = new StringBuilder(";FFMETADATA1\n");
        long previousStart = -1;
        for (var index = 0; index < chapters.Count; index++)
        {
            var chapter = chapters[index];
            var start = (long)Math.Floor(chapter.StartTime.TotalMilliseconds);
            var end = index + 1 < chapters.Count
                ? (long)Math.Floor(chapters[index + 1].StartTime.TotalMilliseconds)
                : durationMilliseconds;
            if (string.IsNullOrWhiteSpace(chapter.Title) || chapter.Title.Length > 500 ||
                chapter.Title.Any(char.IsControl) ||
                start < 0 || start <= previousStart || end <= start || end > durationMilliseconds)
            {
                return Result<string>.Failure(new TubeForgeError(
                    "Media.InvalidChapters",
                    "Chapter timestamps or titles are invalid for this media duration."));
            }

            builder.AppendLine("[CHAPTER]");
            builder.AppendLine("TIMEBASE=1/1000");
            builder.Append("START=").AppendLine(start.ToString(CultureInfo.InvariantCulture));
            builder.Append("END=").AppendLine(end.ToString(CultureInfo.InvariantCulture));
            builder.Append("title=").AppendLine(EscapeMetadataValue(chapter.Title));
            previousStart = start;
        }

        return Result<string>.Success(builder.ToString());
    }

    private static string EscapeMetadataValue(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", string.Empty, StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("=", "\\=", StringComparison.Ordinal)
        .Replace(";", "\\;", StringComparison.Ordinal)
        .Replace("#", "\\#", StringComparison.Ordinal);

    private static TubeForgeError ChapterValidationFailure(int exitCode, string? detail = null) =>
        new(
            "Media.ChapterValidationFailed",
            "FFmpeg did not verify the embedded chapter metadata.",
            detail ?? $"FFmpeg exited with code {exitCode}.");

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void AddStreamCopyOutput(ICollection<string> arguments, MediaContainer container)
    {
        arguments.Add("-c");
        arguments.Add("copy");
        if (container == MediaContainer.Mp4)
        {
            arguments.Add("-movflags");
            arguments.Add("+faststart");
        }

        arguments.Add("-map_metadata");
        arguments.Add("-1");
    }

    private static string ContainerExtension(MediaContainer container) => container switch
    {
        MediaContainer.WebM => "webm",
        MediaContainer.Mkv => "mkv",
        _ => "mp4"
    };

    private static string ContainerFormat(MediaContainer container) => container switch
    {
        MediaContainer.WebM => "webm",
        MediaContainer.Mkv => "matroska",
        _ => "mp4"
    };

    private static string ContainerLabel(MediaContainer container) => container switch
    {
        MediaContainer.WebM => "WebM",
        MediaContainer.Mkv => "MKV",
        _ => "MP4"
    };

    private static string SubtitleCodec(MediaContainer container) => container switch
    {
        MediaContainer.Mp4 => "mov_text",
        MediaContainer.Mkv => "srt",
        MediaContainer.WebM => "webvtt",
        _ => throw new InvalidOperationException("Unsupported subtitle container.")
    };

    internal static TubeForgeError? ValidateOutput(string path, MediaContainer container) => container switch
    {
        MediaContainer.WebM => ValidateEbmlOutput(path, MediaContainer.WebM),
        MediaContainer.Mkv => ValidateEbmlOutput(path, MediaContainer.Mkv),
        _ => ValidateMp4Output(path)
    };

    private static TubeForgeError? ValidateMp4Output(string path)
    {
        var container = MediaContainerValidator.Validate(path, MediaContainer.Mp4);
        if (!container.IsSuccess)
        {
            return container.Error;
        }

        var structure = IsoBmffReader.ReadTopLevel(path);
        if (!structure.IsSuccess)
        {
            return structure.Error;
        }

        var boxes = structure.Value;
        var movieIndex = boxes.ToList().FindIndex(box => box.Type == "moov");
        var mediaIndex = boxes.ToList().FindIndex(box => box.Type == "mdat");
        if (movieIndex < 0 || mediaIndex < 0 || movieIndex > mediaIndex ||
            boxes.Any(box => box.Type == "moof"))
        {
            return new TubeForgeError(
                "Media.IncompatibleMp4Layout",
                "FFmpeg did not produce a conventional indexed MP4.");
        }

        return null;
    }

    private static TubeForgeError? ValidateEbmlOutput(string path, MediaContainer container)
    {
        var header = MediaContainerValidator.Validate(path, container);
        if (!header.IsSuccess)
        {
            return header.Error;
        }

        var document = EbmlReader.ReadDocument(path);
        if (!document.IsSuccess)
        {
            return document.Error;
        }

        return null;
    }

    private static Result<MediaProcessReceipt> Failure(
        string code,
        string message,
        string? detail = null) =>
        Result<MediaProcessReceipt>.Failure(new TubeForgeError(code, message, detail));
}

internal interface IFfmpegProcessRunner
{
    Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken);
}

internal sealed class FfmpegProcessRunner : IFfmpegProcessRunner
{
    public async Task<int> RunAsync(
        string executablePath,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            throw new Win32Exception("FFmpeg did not start.");
        }

        var standardError = process.StandardError.ReadToEndAsync();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        _ = await standardError.ConfigureAwait(false);
        _ = await standardOutput.ConfigureAwait(false);
        return process.ExitCode;
    }
}
