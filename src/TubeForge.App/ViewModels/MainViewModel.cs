using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Runtime.CompilerServices;
using TubeForge.App.Commands;
using TubeForge.Core.Errors;
using TubeForge.Core.Files;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.Downloads;
using TubeForge.Downloads.Queue;
using TubeForge.YouTube;

namespace TubeForge.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly IReadOnlyList<DownloadModeOption> BaseModeChoices =
    [
        new(DownloadMode.AudioVideo, "Audio + video", "Best video + best audio, muxed locally"),
        new(DownloadMode.AudioOnly, "Audio only", "Native M4A/AAC or WebM/Opus audio"),
        new(DownloadMode.VideoOnly, "Video only", "Maximum video quality; audio not included")
    ];

    private static readonly IReadOnlyList<string> AudioProcessingChoices =
    [
        "Native stream · no conversion · no quality loss"
    ];

    private readonly HttpClient _httpClient;
    private readonly YouTubeMetadataResolver _resolver;
    private readonly DirectDownloadEngine _downloader;
    private readonly AdaptiveDownloadEngine _adaptiveDownloader;
    private readonly DownloadQueueStore _queueStore;
    private readonly DownloadQueueDispatcher _queueDispatcher = new(2);
    private readonly SemaphoreSlim _queueMutationLock = new(1, 1);
    private readonly Dictionary<Guid, QueuedDownloadWork> _preparedQueueWork = [];
    private readonly Dictionary<Guid, CancellationTokenSource> _downloadCancellations = [];
    private readonly HashSet<Guid> _cancelledQueueItems = [];
    private CancellationTokenSource? _analysisCancellation;
    private string _urlText = string.Empty;
    private string _downloadFolder;
    private string _videoTitle = string.Empty;
    private string _videoChannel = string.Empty;
    private TimeSpan? _videoDuration;
    private Uri? _thumbnailUrl;
    private string _statusMessage = "Paste a URL to begin";
    private string _progressDetail = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isAnalyzing;
    private bool _isDownloading;
    private double _downloadFraction;
    private FormatItemViewModel? _selectedFormat;
    private VideoMetadata? _metadata;
    private string _extractionStatus = string.Empty;
    private IReadOnlyList<StreamFormat> _allFormats = [];
    private bool _updatingFormatFilters;
    private IReadOnlyList<DownloadModeOption> _downloadModes = BaseModeChoices;
    private DownloadModeOption _selectedDownloadMode = BaseModeChoices[0];
    private IReadOnlyList<FilterOption<int>> _resolutionOptions = [];
    private FilterOption<int>? _selectedResolution;
    private IReadOnlyList<FilterOption<MediaContainer>> _containerOptions = [];
    private FilterOption<MediaContainer>? _selectedContainer;
    private IReadOnlyList<FilterOption<VideoCodec>> _videoCodecOptions = [];
    private FilterOption<VideoCodec>? _selectedVideoCodec;
    private IReadOnlyList<FilterOption<int>> _frameRateOptions = [];
    private FilterOption<int>? _selectedFrameRate;
    private IReadOnlyList<FilterOption<bool>> _dynamicRangeOptions = [];
    private FilterOption<bool>? _selectedDynamicRange;
    private IReadOnlyList<FilterOption<long>> _bitrateOptions = [];
    private FilterOption<long>? _selectedBitrate;
    private IReadOnlyList<FilterOption<AudioCodec>> _audioCodecOptions = [];
    private FilterOption<AudioCodec>? _selectedAudioCodec;
    private string _selectedAudioProcessing = AudioProcessingChoices[0];
    private DownloadQueueSnapshot _queueSnapshot = new();
    private bool _isInitialized;
    private bool _queueUnavailable;
    private string _downloadActionLabel = "Add to queue";
    private bool _isDownloadPage = true;
    private int _selectedMaxConcurrentDownloads = 2;

    public MainViewModel()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _resolver = new YouTubeMetadataResolver(_httpClient);
        _downloader = new DirectDownloadEngine(_httpClient);
        _adaptiveDownloader = new AdaptiveDownloadEngine(_downloader);
        _queueStore = new DownloadQueueStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubeForge",
            "queue.json"));
        _downloadFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsAnalyzing && !string.IsNullOrWhiteSpace(UrlText));
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => !IsAnalyzing && SelectedFormat is not null && HasVideo);
        CancelCommand = new RelayCommand(PauseAll, () => IsDownloading);
        ShowDownloadCommand = new RelayCommand(() => IsDownloadPage = true);
        ShowQueueCommand = new RelayCommand(() => IsDownloadPage = false);
        ClearCompletedCommand = new AsyncRelayCommand(ClearCompletedAsync, () => QueueItems.Any(item => item.Status == DownloadQueueStatus.Completed));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FormatItemViewModel> Formats { get; } = [];

    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = [];

    public IReadOnlyList<DownloadModeOption> DownloadModes => _downloadModes;

    public IReadOnlyList<string> AudioProcessingOptions => AudioProcessingChoices;

    public AsyncRelayCommand AnalyzeCommand { get; }

    public AsyncRelayCommand DownloadCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand ShowDownloadCommand { get; }

    public RelayCommand ShowQueueCommand { get; }

    public AsyncRelayCommand ClearCompletedCommand { get; }

    public IReadOnlyList<int> MaxConcurrentDownloadOptions { get; } = [1, 2, 3, 4];

    public string UrlText
    {
        get => _urlText;
        set
        {
            if (Set(ref _urlText, value)) RefreshCommands();
        }
    }

    public string DownloadFolder
    {
        get => _downloadFolder;
        set => Set(ref _downloadFolder, value);
    }

    public string VideoTitle => _videoTitle;

    public string VideoMetaLine => string.Join("  ·  ", new[]
    {
        _videoChannel,
        _videoDuration is null ? string.Empty : FormatDuration(_videoDuration.Value)
    }.Where(value => !string.IsNullOrWhiteSpace(value)));

    public Uri? ThumbnailUrl => _thumbnailUrl;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    public string ProgressDetail
    {
        get => _progressDetail;
        private set => Set(ref _progressDetail, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (Set(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasVideo => _metadata is not null;

    public bool IsAnalyzing
    {
        get => _isAnalyzing;
        private set
        {
            if (Set(ref _isAnalyzing, value)) BusyStateChanged();
        }
    }

    public bool IsDownloading
    {
        get => _isDownloading;
        private set
        {
            if (Set(ref _isDownloading, value)) BusyStateChanged();
        }
    }

    public bool IsBusy => IsAnalyzing || IsDownloading;

    public bool IsDownloadPage
    {
        get => _isDownloadPage;
        set
        {
            if (Set(ref _isDownloadPage, value))
            {
                OnPropertyChanged(nameof(IsQueuePage));
            }
        }
    }

    public bool IsQueuePage => !IsDownloadPage;

    public bool HasQueueItems => QueueItems.Count > 0;

    public string QueueSummary
    {
        get
        {
            var active = QueueItems.Count(item => item.Status == DownloadQueueStatus.Downloading);
            var waiting = QueueItems.Count(item => item.Status == DownloadQueueStatus.Queued);
            var completed = QueueItems.Count(item => item.Status == DownloadQueueStatus.Completed);
            return $"{active} active · {waiting} waiting · {completed} completed";
        }
    }

    public int SelectedMaxConcurrentDownloads
    {
        get => _selectedMaxConcurrentDownloads;
        set
        {
            if (!Set(ref _selectedMaxConcurrentDownloads, value))
            {
                return;
            }

            _queueDispatcher.MaximumConcurrency = value;
            PumpQueue();
        }
    }

    public double DownloadFraction
    {
        get => _downloadFraction;
        private set => Set(ref _downloadFraction, value);
    }

    public FormatItemViewModel? SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (Set(ref _selectedFormat, value)) RefreshCommands();
        }
    }

    public DownloadModeOption SelectedDownloadMode
    {
        get => _selectedDownloadMode;
        set
        {
            if (value is null || !Set(ref _selectedDownloadMode, value))
            {
                return;
            }

            OnPropertyChanged(nameof(HasVideoFilters));
            OnPropertyChanged(nameof(IsAudioOnly));
            OnPropertyChanged(nameof(IsAudioVideo));
            OnPropertyChanged(nameof(ModeNotice));
            RebuildFormatFilters(resetSelections: true);
        }
    }

    public IReadOnlyList<FilterOption<int>> ResolutionOptions
    {
        get => _resolutionOptions;
        private set => Set(ref _resolutionOptions, value);
    }

    public FilterOption<int>? SelectedResolution
    {
        get => _selectedResolution;
        set => SetFilterSelection(ref _selectedResolution, value);
    }

    public IReadOnlyList<FilterOption<MediaContainer>> ContainerOptions
    {
        get => _containerOptions;
        private set => Set(ref _containerOptions, value);
    }

    public FilterOption<MediaContainer>? SelectedContainer
    {
        get => _selectedContainer;
        set => SetFilterSelection(ref _selectedContainer, value);
    }

    public IReadOnlyList<FilterOption<VideoCodec>> VideoCodecOptions
    {
        get => _videoCodecOptions;
        private set => Set(ref _videoCodecOptions, value);
    }

    public FilterOption<VideoCodec>? SelectedVideoCodec
    {
        get => _selectedVideoCodec;
        set => SetFilterSelection(ref _selectedVideoCodec, value);
    }

    public IReadOnlyList<FilterOption<int>> FrameRateOptions
    {
        get => _frameRateOptions;
        private set => Set(ref _frameRateOptions, value);
    }

    public FilterOption<int>? SelectedFrameRate
    {
        get => _selectedFrameRate;
        set => SetFilterSelection(ref _selectedFrameRate, value);
    }

    public IReadOnlyList<FilterOption<bool>> DynamicRangeOptions
    {
        get => _dynamicRangeOptions;
        private set => Set(ref _dynamicRangeOptions, value);
    }

    public FilterOption<bool>? SelectedDynamicRange
    {
        get => _selectedDynamicRange;
        set => SetFilterSelection(ref _selectedDynamicRange, value);
    }

    public IReadOnlyList<FilterOption<long>> BitrateOptions
    {
        get => _bitrateOptions;
        private set => Set(ref _bitrateOptions, value);
    }

    public FilterOption<long>? SelectedBitrate
    {
        get => _selectedBitrate;
        set => SetFilterSelection(ref _selectedBitrate, value);
    }

    public IReadOnlyList<FilterOption<AudioCodec>> AudioCodecOptions
    {
        get => _audioCodecOptions;
        private set => Set(ref _audioCodecOptions, value);
    }

    public FilterOption<AudioCodec>? SelectedAudioCodec
    {
        get => _selectedAudioCodec;
        set => SetFilterSelection(ref _selectedAudioCodec, value);
    }

    public string SelectedAudioProcessing
    {
        get => _selectedAudioProcessing;
        set => Set(ref _selectedAudioProcessing, value);
    }

    public bool HasVideoFilters => SelectedDownloadMode.Value is not DownloadMode.AudioOnly;

    public bool IsAudioOnly => SelectedDownloadMode.Value is DownloadMode.AudioOnly;

    public bool IsAudioVideo => SelectedDownloadMode.Value is DownloadMode.AudioVideo;

    public string ModeNotice => SelectedDownloadMode.Value switch
    {
        DownloadMode.AudioVideo =>
            CombinedModeNotice(),
        DownloadMode.AudioOnly =>
            "Native AAC/Opus save: fast and lossless. MP3 conversion stays unavailable until TubeForge has its own encoder.",
        DownloadMode.VideoOnly =>
            VideoOnlyModeNotice(),
        _ => string.Empty
    };

    public string FormatCountLabel => $"{Formats.Count} matching · {_allFormats.Count} total";

    public string DownloadActionLabel
    {
        get => _downloadActionLabel;
        private set => Set(ref _downloadActionLabel, value);
    }

    public string ExtractionStatus
    {
        get => _extractionStatus;
        private set => Set(ref _extractionStatus, value);
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        var result = await _queueStore.LoadAsync();
        if (!result.IsSuccess)
        {
            _queueUnavailable = true;
            ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
            StatusMessage = "Queue recovery unavailable";
            return;
        }

        _queueSnapshot = result.Value;
        RebuildQueueItems();
        var recoverableCount = _queueSnapshot.Items.Count(item =>
            item.Status is DownloadQueueStatus.Queued or DownloadQueueStatus.Paused);
        if (recoverableCount > 0)
        {
            StatusMessage = recoverableCount == 1
                ? "Recovered 1 interrupted download"
                : $"Recovered {recoverableCount} interrupted downloads";
        }

        PumpQueue();
    }

    public async Task AnalyzeAsync()
    {
        CancelAnalysis();
        ClearAnalysis();
        var parsed = YouTubeUrlParser.ParseVideoId(UrlText);
        if (!parsed.IsSuccess)
        {
            ErrorMessage = $"{parsed.Error!.Message} ({parsed.Error.Code})";
            StatusMessage = "URL rejected";
            return;
        }

        _analysisCancellation = new CancellationTokenSource();
        IsAnalyzing = true;
        StatusMessage = "Analyzing video…";
        try
        {
            var result = await _resolver.ResolveAsync(parsed.Value, _analysisCancellation.Token);
            if (!result.IsSuccess)
            {
                ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
                StatusMessage = "Analysis failed";
                return;
            }

            _metadata = result.Value.Metadata;
            _videoTitle = _metadata.Title;
            _videoChannel = _metadata.Channel;
            _videoDuration = _metadata.Duration;
            _thumbnailUrl = _metadata.ThumbnailUrl;
            _allFormats = _metadata.Formats;
            RefreshDownloadModes();
            var defaultMode = DownloadModes.FirstOrDefault(choice =>
                HasFormatsForMode(choice.Value)) ??
                DownloadModes[0];
            if (SelectedDownloadMode != defaultMode)
            {
                SelectedDownloadMode = defaultMode;
            }
            else
            {
                RebuildFormatFilters(resetSelections: true);
            }

            ExtractionStatus = result.Value.Diagnostics?.Stage == "AndroidClientResolved"
                ? "DIRECT STREAMS VERIFIED"
                : "WATCH PAGE RESOLVED";
            StatusMessage = Formats.Count > 0
                ? "Choose a stream and download folder"
                : "Metadata found, but no downloadable stream was resolved";
            if (Formats.Count == 0)
            {
                ErrorMessage = "YouTube returned no directly downloadable formats. The player may have changed. (Extractor.NoStreams)";
            }

            NotifyVideoProperties();
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    public async Task DownloadAsync()
    {
        if (_metadata is null || SelectedFormat is null)
        {
            return;
        }

        await InitializeAsync();
        ErrorMessage = string.Empty;
        var selection = SelectedFormat;
        var format = selection.Format;
        try
        {
            var destination = FileNamePolicy.AvailablePath(
                DownloadFolder,
                _metadata.Title,
                selection.RequiresMuxing
                    ? FormatDisplay.Extension(format.Container)
                    : FormatDisplay.OutputExtension(format),
                path => File.Exists(path) ||
                        File.Exists(path + ".part") ||
                        _queueSnapshot.Items.Any(item => item.DestinationPath.Equals(path, StringComparison.OrdinalIgnoreCase)));
            var queueItem = CreateQueueItem(_metadata, selection, destination);
            var queueError = await UpsertQueueItemAsync(queueItem);
            if (queueError is not null)
            {
                ErrorMessage = $"{queueError.Message} ({queueError.Code})";
                StatusMessage = "Download not queued";
                return;
            }

            _preparedQueueWork[queueItem.Id] = new QueuedDownloadWork(_metadata, selection, destination);
            StatusMessage = $"Queued: {Path.GetFileName(destination)}";
            ProgressDetail = $"Global concurrency: {SelectedMaxConcurrentDownloads}";
            PumpQueue();
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = "The download folder or generated filename is invalid. (Queue.InvalidDestination)";
            ProgressDetail = exception.GetType().Name;
            StatusMessage = "Download not queued";
        }
        catch (IOException exception)
        {
            ErrorMessage = "TubeForge could not prepare the queued destination. (Queue.WriteFailed)";
            ProgressDetail = exception.GetType().Name;
            StatusMessage = "Download not queued";
        }
    }

    public void Cancel() => PauseAll();

    public void Dispose()
    {
        CancelAnalysis();
        PauseAll();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private void PumpQueue()
    {
        while (_queueDispatcher.ActiveCount < _queueDispatcher.MaximumConcurrency)
        {
            var next = _queueSnapshot.Items.FirstOrDefault(item =>
                item.Status == DownloadQueueStatus.Queued && !_queueDispatcher.IsActive(item.Id));
            if (next is null || !_queueDispatcher.TryStart(next.Id))
            {
                break;
            }

            IsDownloading = true;
            NotifyQueueProperties();
            _ = RunQueueItemAsync(next.Id);
        }
    }

    private async Task RunQueueItemAsync(Guid itemId)
    {
        var cancellation = new CancellationTokenSource();
        _downloadCancellations[itemId] = cancellation;
        try
        {
            var startError = await UpdateQueueItemAsync(
                itemId,
                DownloadQueueStatus.Downloading,
                failureCode: null,
                cancellationToken: cancellation.Token);
            if (startError is not null)
            {
                ReportQueueError(startError);
                return;
            }

            var prepared = await PrepareQueueWorkAsync(itemId, cancellation.Token);
            if (prepared.Error is not null || prepared.Work is null)
            {
                var status = prepared.Error?.Code == "Operation.Cancelled"
                    ? CancellationStatus(itemId)
                    : DownloadQueueStatus.Failed;
                await CompleteQueueRunAsync(itemId, status, prepared.Error, completedBytes: null);
                return;
            }

            var work = prepared.Work;
            var progress = new Progress<DownloadProgress>(value => UpdateQueueProgress(itemId, value));
            TubeForgeError? downloadError;
            long completedBytes;
            if (work.Selection.AudioFormat is StreamFormat audioFormat)
            {
                var result = await _adaptiveDownloader.DownloadAsync(new AdaptiveDownloadRequest
                {
                    Video = TrackRequest(
                        work.Metadata,
                        work.Selection.Format,
                        IntermediateTrackPath(work.Destination, "video", work.Selection.Format)),
                    Audio = TrackRequest(
                        work.Metadata,
                        audioFormat,
                        IntermediateTrackPath(work.Destination, "audio", audioFormat)),
                    DestinationPath = work.Destination,
                    OutputContainer = work.Selection.Format.Container
                }, progress, cancellation.Token);
                downloadError = result.Error;
                completedBytes = result.IsSuccess ? result.Value.BytesWritten : SelectionPartialLength(work.Destination, work.Selection);
            }
            else
            {
                var result = await _downloader.DownloadAsync(
                    TrackRequest(work.Metadata, work.Selection.Format, work.Destination),
                    progress,
                    cancellation.Token);
                downloadError = result.Error;
                completedBytes = result.IsSuccess ? result.Value.BytesWritten : SelectionPartialLength(work.Destination, work.Selection);
            }

            if (downloadError is not null)
            {
                var status = downloadError.Code == "Operation.Cancelled"
                    ? CancellationStatus(itemId)
                    : DownloadQueueStatus.Failed;
                await CompleteQueueRunAsync(itemId, status, downloadError, completedBytes);
                return;
            }

            await CompleteQueueRunAsync(
                itemId,
                DownloadQueueStatus.Completed,
                error: null,
                completedBytes);
            StatusMessage = "Completed: " + Path.GetFileName(work.Destination);
        }
        catch (OperationCanceledException)
        {
            await CompleteQueueRunAsync(
                itemId,
                CancellationStatus(itemId),
                new TubeForgeError("Operation.Cancelled", "The download was paused."),
                QueuePartialLength(itemId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            await CompleteQueueRunAsync(
                itemId,
                DownloadQueueStatus.Failed,
                new TubeForgeError("Download.UnexpectedFailure", "The queued download failed.", exception.GetType().Name),
                QueuePartialLength(itemId));
        }
        catch (Exception exception)
        {
            await CompleteQueueRunAsync(
                itemId,
                DownloadQueueStatus.Failed,
                new TubeForgeError("Download.InternalFailure", "The queued download hit an internal failure.", exception.GetType().Name),
                QueuePartialLength(itemId));
        }
        finally
        {
            _preparedQueueWork.Remove(itemId);
            _cancelledQueueItems.Remove(itemId);
            _downloadCancellations.Remove(itemId);
            cancellation.Dispose();
            _queueDispatcher.Complete(itemId);
            IsDownloading = _queueDispatcher.ActiveCount > 0;
            NotifyQueueProperties();
            PumpQueue();
        }
    }

    private async Task<(QueuedDownloadWork? Work, TubeForgeError? Error)> PrepareQueueWorkAsync(
        Guid itemId,
        CancellationToken cancellationToken)
    {
        if (_preparedQueueWork.TryGetValue(itemId, out var prepared))
        {
            return (prepared, null);
        }

        var item = _queueSnapshot.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return (null, new TubeForgeError("Queue.ItemMissing", "The queued download no longer exists."));
        }

        if (!YouTubeVideoId.TryCreate(item.VideoId, out var videoId))
        {
            return (null, new TubeForgeError("Queue.InvalidVideoId", "The queued video ID is invalid."));
        }

        var resolved = await _resolver.ResolveAsync(videoId, cancellationToken);
        if (!resolved.IsSuccess)
        {
            return (null, resolved.Error);
        }

        var primary = resolved.Value.Metadata.Formats.FirstOrDefault(format => format.FormatId == item.FormatId);
        if (primary is null)
        {
            return (null, new TubeForgeError(
                "Queue.FormatUnavailable",
                "The queued format is no longer available. Analyze the video again to choose another output."));
        }

        if (!DownloadSourceIdentity.TryParse(item.SourceIdentity, out var sourceIdentity) ||
            sourceIdentity.VideoId != videoId || sourceIdentity.PrimaryFormatId != item.FormatId)
        {
            return (null, new TubeForgeError("Queue.InvalidSourceIdentity", "The queued source identity is invalid."));
        }

        StreamFormat? audio = null;
        var audioFormatId = sourceIdentity.AudioFormatId;
        if (audioFormatId is not null)
        {
            audio = resolved.Value.Metadata.Formats.FirstOrDefault(format =>
                format.FormatId == audioFormatId && format.Kind == StreamKind.AudioOnly);
            if (audio is null || !AdaptiveFormatSelector.AreMuxCompatible(primary, audio))
            {
                return (null, new TubeForgeError(
                    "Queue.AudioFormatUnavailable",
                    "The queued companion audio format is no longer available."));
            }
        }

        var selection = new FormatItemViewModel(primary, audio);
        var work = new QueuedDownloadWork(resolved.Value.Metadata, selection, item.DestinationPath);
        _preparedQueueWork[itemId] = work;
        return (work, null);
    }

    private async Task CompleteQueueRunAsync(
        Guid itemId,
        DownloadQueueStatus status,
        TubeForgeError? error,
        long? completedBytes)
    {
        var queueError = await UpdateQueueItemAsync(itemId, status, error?.Code, completedBytes);
        if (queueError is not null)
        {
            ReportQueueError(queueError);
            return;
        }

        if (error is not null && error.Code != "Operation.Cancelled")
        {
            ErrorMessage = $"{error.Message} ({error.Code})";
        }
    }

    private void UpdateQueueProgress(Guid itemId, DownloadProgress progress)
    {
        var card = QueueItems.FirstOrDefault(item => item.Id == itemId);
        card?.UpdateProgress(progress);
        DownloadFraction = progress.Fraction ?? 0;
        ProgressDetail = $"{_queueDispatcher.ActiveCount} active · global limit {SelectedMaxConcurrentDownloads}";
    }

    private long? QueuePartialLength(Guid itemId)
    {
        if (_preparedQueueWork.TryGetValue(itemId, out var work))
        {
            return SelectionPartialLength(work.Destination, work.Selection);
        }

        return _queueSnapshot.Items.FirstOrDefault(item => item.Id == itemId)?.BytesReceived;
    }

    private async Task ResumeQueueItemAsync(Guid itemId)
    {
        var item = _queueSnapshot.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || item.Status is not (DownloadQueueStatus.Paused or DownloadQueueStatus.Failed or DownloadQueueStatus.Cancelled))
        {
            return;
        }

        ErrorMessage = string.Empty;
        var error = await UpdateQueueItemAsync(itemId, DownloadQueueStatus.Queued, failureCode: null);
        if (error is not null)
        {
            ReportQueueError(error);
            return;
        }

        PumpQueue();
    }

    private void PauseQueueItem(Guid itemId)
    {
        if (_downloadCancellations.TryGetValue(itemId, out var cancellation))
        {
            cancellation.Cancel();
        }
    }

    private async Task CancelQueueItemAsync(Guid itemId)
    {
        var item = _queueSnapshot.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null || item.Status is not (DownloadQueueStatus.Queued or DownloadQueueStatus.Downloading))
        {
            return;
        }

        _cancelledQueueItems.Add(itemId);
        if (_downloadCancellations.TryGetValue(itemId, out var cancellation))
        {
            cancellation.Cancel();
            return;
        }

        var error = await UpdateQueueItemAsync(itemId, DownloadQueueStatus.Cancelled, "Operation.Cancelled");
        _cancelledQueueItems.Remove(itemId);
        if (error is not null)
        {
            ReportQueueError(error);
        }
    }

    private void PauseAll()
    {
        foreach (var cancellation in _downloadCancellations.Values.ToArray())
        {
            cancellation.Cancel();
        }
    }

    private async Task RemoveQueueItemAsync(Guid itemId)
    {
        if (_queueDispatcher.IsActive(itemId))
        {
            return;
        }

        await RemoveQueueItemsAsync([itemId]);
    }

    private async Task ClearCompletedAsync()
    {
        var completedIds = _queueSnapshot.Items
            .Where(item => item.Status == DownloadQueueStatus.Completed)
            .Select(item => item.Id)
            .ToArray();
        await RemoveQueueItemsAsync(completedIds);
    }

    private async Task RemoveQueueItemsAsync(IReadOnlyCollection<Guid> itemIds)
    {
        if (itemIds.Count == 0 || _queueUnavailable)
        {
            return;
        }

        await _queueMutationLock.WaitAsync();
        try
        {
            var ids = itemIds.ToHashSet();
            var nextSnapshot = _queueSnapshot with
            {
                Items = _queueSnapshot.Items.Where(item => !ids.Contains(item.Id)).ToArray()
            };
            var result = await _queueStore.SaveAsync(nextSnapshot);
            if (!result.IsSuccess)
            {
                ReportQueueError(result.Error!);
                return;
            }

            _queueSnapshot = nextSnapshot;
            foreach (var card in QueueItems.Where(item => ids.Contains(item.Id)).ToArray())
            {
                QueueItems.Remove(card);
            }

            foreach (var id in ids)
            {
                _preparedQueueWork.Remove(id);
            }

            NotifyQueueProperties();
        }
        finally
        {
            _queueMutationLock.Release();
        }
    }

    private void RebuildQueueItems()
    {
        QueueItems.Clear();
        foreach (var item in _queueSnapshot.Items.OrderBy(item => item.CreatedAtUtc))
        {
            QueueItems.Add(CreateQueueCard(item));
        }

        NotifyQueueProperties();
    }

    private void RefreshQueueItem(DownloadQueueItem item)
    {
        var existing = QueueItems.FirstOrDefault(candidate => candidate.Id == item.Id);
        if (existing is null)
        {
            QueueItems.Add(CreateQueueCard(item));
        }
        else
        {
            existing.Update(item);
        }

        NotifyQueueProperties();
    }

    private QueueItemViewModel CreateQueueCard(DownloadQueueItem item) => new(
        item,
        ResumeQueueItemAsync,
        PauseQueueItem,
        CancelQueueItemAsync,
        RemoveQueueItemAsync,
        RevealDestination);

    private DownloadQueueStatus CancellationStatus(Guid itemId) =>
        _cancelledQueueItems.Contains(itemId)
            ? DownloadQueueStatus.Cancelled
            : DownloadQueueStatus.Paused;

    private void NotifyQueueProperties()
    {
        OnPropertyChanged(nameof(HasQueueItems));
        OnPropertyChanged(nameof(QueueSummary));
        ClearCompletedCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void RevealDestination(string destination)
    {
        try
        {
            var directory = Path.GetDirectoryName(destination);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                ErrorMessage = "The destination folder no longer exists. (Queue.DirectoryMissing)";
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            ErrorMessage = $"Windows could not open the destination folder. (Queue.RevealFailed: {exception.GetType().Name})";
        }
    }

    private void ReportQueueError(TubeForgeError error)
    {
        ErrorMessage = $"{error.Message} ({error.Code})";
        StatusMessage = "Queue state warning";
    }

    private static DownloadQueueItem CreateQueueItem(
        VideoMetadata metadata,
        FormatItemViewModel selection,
        string destination)
    {
        var now = DateTimeOffset.UtcNow;
        var format = selection.Format;
        var sourceIdentity = SelectionIdentity(metadata, selection);
        var expectedLength = CombinedLength(selection);
        var partialLength = SelectionPartialLength(destination, selection);

        return new DownloadQueueItem
        {
            Id = Guid.NewGuid(),
            VideoId = metadata.Id.Value,
            FormatId = format.FormatId,
            SourceIdentity = sourceIdentity,
            DisplayTitle = metadata.Title,
            DestinationPath = destination,
            ExpectedLength = expectedLength,
            BytesReceived = partialLength,
            Status = DownloadQueueStatus.Queued,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    private static DownloadRequest TrackRequest(
        VideoMetadata metadata,
        StreamFormat format,
        string destination) => new()
        {
            SourceUrl = format.Url,
            SourceIdentity = $"{metadata.Id.Value}:{format.FormatId}",
            DestinationPath = destination,
            ExpectedLength = format.ContentLength,
            ExpectedContainer = format.Container
        };

    private static string IntermediateTrackPath(
        string destination,
        string role,
        StreamFormat format) =>
        destination + $".{role}-track" + FormatDisplay.OutputExtension(format);

    private static string SelectionIdentity(VideoMetadata metadata, FormatItemViewModel selection) =>
        DownloadSourceIdentity.Create(
            metadata.Id,
            selection.Format.FormatId,
            selection.AudioFormat?.FormatId);

    private static long? CombinedLength(FormatItemViewModel selection) =>
        selection.Format.ContentLength is not null && selection.AudioFormat?.ContentLength is not null
            ? checked(selection.Format.ContentLength.Value + selection.AudioFormat.ContentLength.Value)
            : selection.AudioFormat is null ? selection.Format.ContentLength : null;

    private static long SelectionPartialLength(string destination, FormatItemViewModel selection)
    {
        if (selection.AudioFormat is null)
        {
            return PartialLength(destination);
        }

        var videoPath = IntermediateTrackPath(destination, "video", selection.Format);
        var audioPath = IntermediateTrackPath(destination, "audio", selection.AudioFormat);
        return checked(CompletedOrPartialLength(videoPath) + CompletedOrPartialLength(audioPath));
    }

    private static long CompletedOrPartialLength(string destination)
    {
        try
        {
            if (File.Exists(destination))
            {
                return new FileInfo(destination).Length;
            }

            return PartialLength(destination);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private async Task<TubeForgeError?> UpsertQueueItemAsync(
        DownloadQueueItem item,
        CancellationToken cancellationToken = default)
    {
        if (_queueUnavailable)
        {
            return new TubeForgeError(
                "Queue.Unavailable",
                "The saved queue must be repaired before starting another download.");
        }

        await _queueMutationLock.WaitAsync(cancellationToken);
        try
        {
            var nextSnapshot = _queueSnapshot with
            {
                Items = _queueSnapshot.Items
                    .Where(existing => existing.Id != item.Id)
                    .Append(item)
                    .OrderBy(existing => existing.CreatedAtUtc)
                    .ToArray()
            };
            var result = await _queueStore.SaveAsync(nextSnapshot, cancellationToken);
            if (!result.IsSuccess)
            {
                return result.Error;
            }

            _queueSnapshot = nextSnapshot;
            RefreshQueueItem(item);
            return null;
        }
        finally
        {
            _queueMutationLock.Release();
        }
    }

    private async Task<TubeForgeError?> UpdateQueueItemAsync(
        Guid itemId,
        DownloadQueueStatus status,
        string? failureCode,
        long? completedBytes = null,
        CancellationToken cancellationToken = default)
    {
        var item = _queueSnapshot.Items.FirstOrDefault(candidate => candidate.Id == itemId);
        if (item is null)
        {
            return new TubeForgeError("Queue.ItemMissing", "The queued download no longer exists.");
        }

        return await UpsertQueueItemAsync(item with
        {
            BytesReceived = completedBytes ?? item.BytesReceived,
            Status = status,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
            FailureCode = failureCode
        }, cancellationToken);
    }

    private static long PartialLength(string destination)
    {
        try
        {
            var partialPath = destination + ".part";
            return File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private void ClearAnalysis()
    {
        _metadata = null;
        _videoTitle = string.Empty;
        _videoChannel = string.Empty;
        _videoDuration = null;
        _thumbnailUrl = null;
        _allFormats = [];
        _downloadModes = BaseModeChoices;
        OnPropertyChanged(nameof(DownloadModes));
        SelectedDownloadMode = BaseModeChoices[0];
        ExtractionStatus = string.Empty;
        ErrorMessage = string.Empty;
        ProgressDetail = string.Empty;
        Formats.Clear();
        SelectedFormat = null;
        DownloadActionLabel = "Add to queue";
        RebuildFormatFilters(resetSelections: true);
        NotifyVideoProperties();
    }

    private void RebuildFormatFilters(bool resetSelections)
    {
        if (_updatingFormatFilters)
        {
            return;
        }

        _updatingFormatFilters = true;
        try
        {
            var modeFormats = IsAudioVideo
                ? AudioVideoVideoFormats()
                : FormatFilter.Apply(_allFormats, new FormatSelectionCriteria
                {
                    Mode = SelectedDownloadMode.Value
                });

            if (IsAudioOnly)
            {
                ResolutionOptions = [];
                SelectedResolution = null;
                VideoCodecOptions = [];
                SelectedVideoCodec = null;
                FrameRateOptions = [];
                SelectedFrameRate = null;
                DynamicRangeOptions = [];
                SelectedDynamicRange = null;

                BitrateOptions = WithAny(
                    "Best available",
                    modeFormats
                        .Where(format => format.Bitrate is > 0)
                        .Select(format => format.Bitrate!.Value)
                        .Distinct()
                        .OrderByDescending(value => value)
                        .Select(value => new FilterOption<long>($"{Math.Round(value / 1000d):0} kbps", value)));
                SelectedBitrate = Choose(
                    BitrateOptions,
                    resetSelections ? null : SelectedBitrate?.Value);

                var bitrateFormats = FilterBy(modeFormats, bitrate: SelectedBitrate?.Value);
                ContainerOptions = WithAny(
                    "Any native format",
                    bitrateFormats
                        .Select(format => format.Container)
                        .Distinct()
                        .OrderBy(ContainerOrder)
                        .Select(value => new FilterOption<MediaContainer>(AudioContainerLabel(value), value)));
                SelectedContainer = Choose(
                    ContainerOptions,
                    resetSelections ? null : SelectedContainer?.Value);

                var containerFormats = FilterBy(
                    bitrateFormats,
                    container: SelectedContainer?.Value);
                AudioCodecOptions = WithAny(
                    "Any audio codec",
                    containerFormats
                        .Select(format => format.AudioCodec)
                        .Distinct()
                        .OrderBy(AudioCodecOrder)
                        .Select(value => new FilterOption<AudioCodec>(AudioCodecLabel(value), value)));
                SelectedAudioCodec = Choose(
                    AudioCodecOptions,
                    resetSelections ? null : SelectedAudioCodec?.Value);
            }
            else
            {
                ResolutionOptions = WithAny(
                    "Best available",
                    modeFormats
                        .Where(format => format.Height is > 0)
                        .Select(format => format.Height!.Value)
                        .Distinct()
                        .OrderByDescending(value => value)
                        .Select(value => new FilterOption<int>(ResolutionLabel(value), value)));
                SelectedResolution = Choose(
                    ResolutionOptions,
                    resetSelections ? null : SelectedResolution?.Value);

                var resolutionFormats = FilterBy(modeFormats, height: SelectedResolution?.Value);
                ContainerOptions = WithAny(
                    "Any container · MP4 preferred",
                    resolutionFormats
                        .Select(format => format.Container)
                        .Distinct()
                        .OrderBy(ContainerOrder)
                        .Select(value => new FilterOption<MediaContainer>(ContainerLabel(value), value)));
                SelectedContainer = Choose(
                    ContainerOptions,
                    resetSelections ? null : SelectedContainer?.Value);

                var containerFormats = FilterBy(
                    resolutionFormats,
                    container: SelectedContainer?.Value);
                VideoCodecOptions = WithAny(
                    "Any video codec",
                    containerFormats
                        .Select(format => format.VideoCodec)
                        .Distinct()
                        .OrderBy(VideoCodecOrder)
                        .Select(value => new FilterOption<VideoCodec>(VideoCodecLabel(value), value)));
                SelectedVideoCodec = Choose(
                    VideoCodecOptions,
                    resetSelections ? null : SelectedVideoCodec?.Value);

                var codecFormats = FilterBy(
                    containerFormats,
                    videoCodec: SelectedVideoCodec?.Value);
                FrameRateOptions = WithAny(
                    "Any frame rate",
                    codecFormats
                        .Where(format => format.FramesPerSecond is > 0)
                        .Select(format => format.FramesPerSecond!.Value)
                        .Distinct()
                        .OrderByDescending(value => value)
                        .Select(value => new FilterOption<int>($"{value} FPS", value)));
                SelectedFrameRate = Choose(
                    FrameRateOptions,
                    resetSelections ? null : SelectedFrameRate?.Value);

                var frameRateFormats = FilterBy(
                    codecFormats,
                    framesPerSecond: SelectedFrameRate?.Value);
                DynamicRangeOptions = WithAny(
                    "Any dynamic range",
                    frameRateFormats
                        .Select(format => format.IsHdr)
                        .Distinct()
                        .OrderByDescending(value => value)
                        .Select(value => new FilterOption<bool>(value ? "HDR" : "SDR", value)));
                SelectedDynamicRange = Choose(
                    DynamicRangeOptions,
                    resetSelections ? null : SelectedDynamicRange?.Value);

                if (IsAudioVideo)
                {
                    var selectedVideoFormats = frameRateFormats
                        .Where(format => SelectedDynamicRange?.Value is null ||
                                         format.IsHdr == SelectedDynamicRange.Value.Value)
                        .ToArray();
                    var audioCandidates = CompatibleAudioFormats(selectedVideoFormats);
                    BitrateOptions = WithAny(
                        "Best available",
                        audioCandidates
                            .Where(format => format.Bitrate is > 0)
                            .Select(format => format.Bitrate!.Value)
                            .Distinct()
                            .OrderByDescending(value => value)
                            .Select(value => new FilterOption<long>($"{Math.Round(value / 1000d):0} kbps", value)));
                    SelectedBitrate = Choose(
                        BitrateOptions,
                        resetSelections ? null : SelectedBitrate?.Value);

                    var bitrateFormats = FilterBy(audioCandidates, bitrate: SelectedBitrate?.Value);
                    AudioCodecOptions = WithAny(
                        "Best compatible codec",
                        bitrateFormats
                            .Select(format => format.AudioCodec)
                            .Distinct()
                            .OrderBy(AudioCodecOrder)
                            .Select(value => new FilterOption<AudioCodec>(AudioCodecLabel(value), value)));
                    SelectedAudioCodec = Choose(
                        AudioCodecOptions,
                        resetSelections ? null : SelectedAudioCodec?.Value);
                }
                else
                {
                    BitrateOptions = [];
                    SelectedBitrate = null;
                    AudioCodecOptions = [];
                    SelectedAudioCodec = null;
                }
            }
        }
        finally
        {
            _updatingFormatFilters = false;
        }

        RefreshMatchingFormats();
    }

    private void RefreshDownloadModes()
    {
        var combined = AudioVideoVideoFormats();
        var audio = FormatsForMode(DownloadMode.AudioOnly);
        var video = FormatsForMode(DownloadMode.VideoOnly);

        _downloadModes =
        [
            new DownloadModeOption(
                DownloadMode.AudioVideo,
                "Audio + video",
                $"{OptionCount(combined.Count)} · up to {MaximumVideoQuality(combined)} · best compatible audio"),
            new DownloadModeOption(
                DownloadMode.AudioOnly,
                "Audio only",
                $"{OptionCount(audio.Count)} · up to {MaximumAudioBitrate(audio)} · M4A/WebM"),
            new DownloadModeOption(
                DownloadMode.VideoOnly,
                "Video only",
                $"{OptionCount(video.Count)} · up to {MaximumVideoQuality(video)} · no audio")
        ];
        OnPropertyChanged(nameof(DownloadModes));
    }

    private IReadOnlyList<StreamFormat> FormatsForMode(DownloadMode mode) =>
        FormatFilter.Apply(_allFormats, new FormatSelectionCriteria { Mode = mode });

    private bool HasFormatsForMode(DownloadMode mode) => mode == DownloadMode.AudioVideo
        ? AudioVideoVideoFormats().Count > 0
        : FormatsForMode(mode).Count > 0;

    private IReadOnlyList<StreamFormat> AudioVideoVideoFormats()
    {
        var audio = _allFormats.Where(format => format.Kind == StreamKind.AudioOnly).ToArray();
        return _allFormats
            .Where(format => format.Kind == StreamKind.Progressive ||
                             format.Kind == StreamKind.VideoOnly &&
                             audio.Any(candidate => AdaptiveFormatSelector.AreMuxCompatible(format, candidate)))
            .OrderByDescending(format => format.Kind == StreamKind.VideoOnly)
            .ThenByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.FramesPerSecond ?? 0)
            .ThenByDescending(format => format.IsHdr)
            .ThenByDescending(format => format.Container == MediaContainer.Mp4)
            .ThenByDescending(format => format.Bitrate ?? 0)
            .ThenBy(format => format.FormatId)
            .ToArray();
    }

    private IReadOnlyList<StreamFormat> CompatibleAudioFormats(IEnumerable<StreamFormat> videos)
    {
        var materializedVideos = videos.Where(format => format.Kind == StreamKind.VideoOnly).ToArray();
        return _allFormats
            .Where(audio => audio.Kind == StreamKind.AudioOnly &&
                            materializedVideos.Any(video => AdaptiveFormatSelector.AreMuxCompatible(video, audio)))
            .OrderByDescending(audio => audio.Bitrate ?? 0)
            .ThenByDescending(audio => audio.AudioSampleRate ?? 0)
            .ThenBy(audio => audio.FormatId)
            .ToArray();
    }

    private string CombinedModeNotice()
    {
        var combined = AudioVideoVideoFormats();
        return $"Up to {MaximumVideoQuality(combined)} with audio. TubeForge downloads the selected video and best compatible audio separately, then muxes both locally without re-encoding.";
    }

    private string VideoOnlyModeNotice() =>
        $"Up to {MaximumVideoQuality(FormatsForMode(DownloadMode.VideoOnly))}. " +
        "Video track only: no audio. Choose Audio + video for a locally muxed file with both tracks.";

    private void RefreshMatchingFormats()
    {
        if (IsAudioVideo)
        {
            var videos = AudioVideoVideoFormats()
                .Where(format =>
                    (SelectedResolution?.Value is null || format.Height == SelectedResolution.Value.Value) &&
                    (SelectedContainer?.Value is null || format.Container == SelectedContainer.Value.Value) &&
                    (SelectedVideoCodec?.Value is null || format.VideoCodec == SelectedVideoCodec.Value.Value) &&
                    (SelectedFrameRate?.Value is null || format.FramesPerSecond == SelectedFrameRate.Value.Value) &&
                    (SelectedDynamicRange?.Value is null || format.IsHdr == SelectedDynamicRange.Value.Value))
                .ToArray();
            var audio = _allFormats
                .Where(format => format.Kind == StreamKind.AudioOnly &&
                                 (SelectedBitrate?.Value is null || format.Bitrate == SelectedBitrate.Value.Value) &&
                                 (SelectedAudioCodec?.Value is null || format.AudioCodec == SelectedAudioCodec.Value.Value))
                .OrderByDescending(format => format.Bitrate ?? 0)
                .ThenByDescending(format => format.AudioSampleRate ?? 0)
                .ThenBy(format => format.FormatId)
                .ToArray();

            Formats.Clear();
            foreach (var video in videos)
            {
                if (video.Kind == StreamKind.Progressive)
                {
                    Formats.Add(new FormatItemViewModel(video));
                    continue;
                }

                var companion = audio.FirstOrDefault(candidate =>
                    AdaptiveFormatSelector.AreMuxCompatible(video, candidate));
                if (companion is not null)
                {
                    Formats.Add(new FormatItemViewModel(video, companion));
                }
            }

            CompleteFormatRefresh();
            return;
        }

        var criteria = new FormatSelectionCriteria
        {
            Mode = SelectedDownloadMode.Value,
            Height = HasVideoFilters ? SelectedResolution?.Value : null,
            Container = SelectedContainer?.Value,
            VideoCodec = HasVideoFilters ? SelectedVideoCodec?.Value : null,
            AudioCodec = IsAudioOnly ? SelectedAudioCodec?.Value : null,
            FramesPerSecond = HasVideoFilters ? SelectedFrameRate?.Value : null,
            Bitrate = IsAudioOnly ? SelectedBitrate?.Value : null,
            IsHdr = HasVideoFilters ? SelectedDynamicRange?.Value : null
        };

        Formats.Clear();
        foreach (var format in FormatFilter.Apply(_allFormats, criteria))
        {
            Formats.Add(new FormatItemViewModel(format));
        }

        CompleteFormatRefresh();
    }

    private void CompleteFormatRefresh()
    {
        SelectedFormat = Formats.FirstOrDefault();
        OnPropertyChanged(nameof(FormatCountLabel));
        if (_metadata is not null && !IsBusy)
        {
            StatusMessage = Formats.Count > 0
                ? "Choose exact output, then download"
                : "No output matches these filters";
        }
    }

    private void SetFilterSelection<T>(
        ref FilterOption<T>? field,
        FilterOption<T>? value,
        [CallerMemberName] string? propertyName = null) where T : struct
    {
        if (!Set(ref field, value, propertyName) || _updatingFormatFilters)
        {
            return;
        }

        RebuildFormatFilters(resetSelections: false);
    }

    private void NotifyVideoProperties()
    {
        OnPropertyChanged(nameof(HasVideo));
        OnPropertyChanged(nameof(VideoTitle));
        OnPropertyChanged(nameof(VideoMetaLine));
        OnPropertyChanged(nameof(ThumbnailUrl));
        OnPropertyChanged(nameof(FormatCountLabel));
        RefreshCommands();
    }

    private void BusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        DownloadCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }

    private void CancelAnalysis()
    {
        _analysisCancellation?.Cancel();
        _analysisCancellation?.Dispose();
        _analysisCancellation = null;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");

    private static string FormatBytes(long bytes)
    {
        var megabytes = bytes / 1024d / 1024d;
        return megabytes >= 1024 ? $"{megabytes / 1024:0.00} GB" : $"{megabytes:0.0} MB";
    }

    private static string OptionCount(int count) => count == 1 ? "1 option" : $"{count} options";

    private static string MaximumVideoQuality(IEnumerable<StreamFormat> formats)
    {
        var height = formats.Select(format => format.Height ?? 0).DefaultIfEmpty(0).Max();
        return height > 0 ? ResolutionLabel(height) : "unavailable";
    }

    private static string MaximumAudioBitrate(IEnumerable<StreamFormat> formats)
    {
        var bitrate = formats.Select(format => format.Bitrate ?? 0).DefaultIfEmpty(0).Max();
        return bitrate > 0 ? $"{Math.Round(bitrate / 1000d):0} kbps" : "unknown bitrate";
    }

    private static IReadOnlyList<FilterOption<T>> WithAny<T>(
        string label,
        IEnumerable<FilterOption<T>> options) where T : struct =>
        [new FilterOption<T>(label, null), .. options];

    private static FilterOption<T>? Choose<T>(
        IReadOnlyList<FilterOption<T>> options,
        T? desired) where T : struct =>
        desired is null
            ? options.FirstOrDefault()
            : options.FirstOrDefault(option => EqualityComparer<T?>.Default.Equals(option.Value, desired)) ??
              options.FirstOrDefault();

    private static IReadOnlyList<StreamFormat> FilterBy(
        IEnumerable<StreamFormat> formats,
        int? height = null,
        MediaContainer? container = null,
        VideoCodec? videoCodec = null,
        int? framesPerSecond = null,
        long? bitrate = null) =>
        formats.Where(format =>
            (height is null || format.Height == height) &&
            (container is null || format.Container == container) &&
            (videoCodec is null || format.VideoCodec == videoCodec) &&
            (framesPerSecond is null || format.FramesPerSecond == framesPerSecond) &&
            (bitrate is null || format.Bitrate == bitrate))
        .ToArray();

    private static string ResolutionLabel(int height) => height switch
    {
        >= 4320 => $"{height}p · 8K",
        >= 2160 => $"{height}p · 4K",
        >= 1440 => $"{height}p · 2K",
        >= 1080 => $"{height}p · Full HD",
        >= 720 => $"{height}p · HD",
        _ => $"{height}p"
    };

    private static string ContainerLabel(MediaContainer container) => container switch
    {
        MediaContainer.Mp4 => "MP4",
        MediaContainer.WebM => "WebM",
        MediaContainer.ThreeGp => "3GP",
        _ => "Unknown container"
    };

    private static string AudioContainerLabel(MediaContainer container) => container switch
    {
        MediaContainer.Mp4 => "M4A / MP4",
        MediaContainer.WebM => "WebM",
        MediaContainer.ThreeGp => "3GP",
        _ => "Unknown format"
    };

    private static string VideoCodecLabel(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => "H.264 · broad compatibility",
        VideoCodec.Vp9 => "VP9 · efficient",
        VideoCodec.Av1 => "AV1 · most efficient",
        VideoCodec.Unknown => "Unknown codec",
        _ => "No video codec"
    };

    private static string AudioCodecLabel(AudioCodec codec) => codec switch
    {
        AudioCodec.Aac => "AAC · broad compatibility",
        AudioCodec.Opus => "Opus · efficient",
        AudioCodec.Vorbis => "Vorbis",
        AudioCodec.Unknown => "Unknown codec",
        _ => "No audio codec"
    };

    private static int ContainerOrder(MediaContainer container) => container switch
    {
        MediaContainer.Mp4 => 0,
        MediaContainer.WebM => 1,
        MediaContainer.ThreeGp => 2,
        _ => 3
    };

    private static int VideoCodecOrder(VideoCodec codec) => codec switch
    {
        VideoCodec.H264 => 0,
        VideoCodec.Vp9 => 1,
        VideoCodec.Av1 => 2,
        _ => 3
    };

    private static int AudioCodecOrder(AudioCodec codec) => codec switch
    {
        AudioCodec.Aac => 0,
        AudioCodec.Opus => 1,
        AudioCodec.Vorbis => 2,
        _ => 3
    };

    private sealed record QueuedDownloadWork(
        VideoMetadata Metadata,
        FormatItemViewModel Selection,
        string Destination);
}
