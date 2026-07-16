using TubeForge.Core.Errors;
using TubeForge.Core.Media;
using TubeForge.Core.Results;
using TubeForge.Media;
using TubeForge.Media.Ebml;
using TubeForge.Media.IsoBmff;

namespace TubeForge.Downloads;

public sealed class AdaptiveDownloadEngine(DirectDownloadEngine directDownloadEngine)
{
    private readonly DirectDownloadEngine _directDownloadEngine =
        directDownloadEngine ?? throw new ArgumentNullException(nameof(directDownloadEngine));

    public async Task<Result<AdaptiveDownloadReceipt>> DownloadAsync(
        AdaptiveDownloadRequest request,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateRequest(request);
        if (validation is not null)
        {
            return Result<AdaptiveDownloadReceipt>.Failure(validation);
        }

        var totalBytes = Sum(request.Video.ExpectedLength, request.Audio.ExpectedLength);
        var videoProgress = ProgressFor(progress, 0, totalBytes);
        var videoResult = await EnsureTrackAsync(request.Video, videoProgress, cancellationToken)
            .ConfigureAwait(false);
        if (!videoResult.IsSuccess)
        {
            return Result<AdaptiveDownloadReceipt>.Failure(videoResult.Error!);
        }

        var audioProgress = ProgressFor(progress, videoResult.Value.BytesWritten, totalBytes);
        var audioResult = await EnsureTrackAsync(request.Audio, audioProgress, cancellationToken)
            .ConfigureAwait(false);
        if (!audioResult.IsSuccess)
        {
            return Result<AdaptiveDownloadReceipt>.Failure(audioResult.Error!);
        }

        Result<(string Path, long Length)> muxResult;
        if (request.OutputContainer == MediaContainer.Mp4)
        {
            var result = await Mp4TrackMuxer.MuxAsync(
                    request.Video.DestinationPath,
                    request.Audio.DestinationPath,
                    request.DestinationPath,
                    cancellationToken)
                .ConfigureAwait(false);
            muxResult = result.IsSuccess
                ? Result<(string Path, long Length)>.Success((result.Value.DestinationPath, result.Value.BytesWritten))
                : Result<(string Path, long Length)>.Failure(result.Error!);
        }
        else
        {
            var result = await WebMTrackMuxer.MuxAsync(
                    request.Video.DestinationPath,
                    request.Audio.DestinationPath,
                    request.DestinationPath,
                    cancellationToken)
                .ConfigureAwait(false);
            muxResult = result.IsSuccess
                ? Result<(string Path, long Length)>.Success((result.Value.DestinationPath, result.Value.BytesWritten))
                : Result<(string Path, long Length)>.Failure(result.Error!);
        }

        if (!muxResult.IsSuccess)
        {
            return Result<AdaptiveDownloadReceipt>.Failure(muxResult.Error!);
        }

        File.Delete(request.Video.DestinationPath);
        File.Delete(request.Audio.DestinationPath);
        progress?.Report(new DownloadProgress(
            totalBytes ?? videoResult.Value.BytesWritten + audioResult.Value.BytesWritten,
            totalBytes,
            0,
            TimeSpan.Zero));
        return Result<AdaptiveDownloadReceipt>.Success(new AdaptiveDownloadReceipt(
            muxResult.Value.Path,
            muxResult.Value.Length,
            videoResult.Value.BytesWritten,
            audioResult.Value.BytesWritten));
    }

    private async Task<Result<DownloadReceipt>> EnsureTrackAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.DestinationPath))
        {
            return await _directDownloadEngine.DownloadAsync(request, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        var length = new FileInfo(request.DestinationPath).Length;
        if (request.ExpectedLength is not null && request.ExpectedLength != length)
        {
            return Result<DownloadReceipt>.Failure(new TubeForgeError(
                "Media.IntermediateConflict",
                "A completed intermediate track has an unexpected size."));
        }

        var validation = MediaContainerValidator.Validate(request.DestinationPath, request.ExpectedContainer);
        if (!validation.IsSuccess)
        {
            return Result<DownloadReceipt>.Failure(validation.Error!);
        }

        progress?.Report(new DownloadProgress(length, request.ExpectedLength ?? length, 0, TimeSpan.Zero));
        return Result<DownloadReceipt>.Success(new DownloadReceipt(request.DestinationPath, length, Resumed: true));
    }

    private static TubeForgeError? ValidateRequest(AdaptiveDownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Video);
        ArgumentNullException.ThrowIfNull(request.Audio);
        if (request.OutputContainer is not (MediaContainer.Mp4 or MediaContainer.WebM) ||
            request.Video.ExpectedContainer != request.OutputContainer ||
            request.Audio.ExpectedContainer != request.OutputContainer)
        {
            return new TubeForgeError(
                "Media.IncompatibleTracks",
                "Adaptive video and audio must use one supported output container.");
        }

        if (string.IsNullOrWhiteSpace(request.DestinationPath))
        {
            return new TubeForgeError("Download.InvalidDestination", "Select an adaptive output file.");
        }

        return null;
    }

    private static IProgress<DownloadProgress>? ProgressFor(
        IProgress<DownloadProgress>? target,
        long completedBytes,
        long? totalBytes) =>
        target is null
            ? null
            : new InlineProgress(value =>
            {
                var received = checked(completedBytes + value.BytesReceived);
                var remaining = totalBytes is not null && value.BytesPerSecond > 0
                    ? TimeSpan.FromSeconds(Math.Max(0, totalBytes.Value - received) / value.BytesPerSecond)
                    : value.EstimatedRemaining;
                target.Report(new DownloadProgress(received, totalBytes, value.BytesPerSecond, remaining));
            });

    private static long? Sum(long? first, long? second) =>
        first is not null && second is not null ? checked(first.Value + second.Value) : null;

    private sealed class InlineProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
