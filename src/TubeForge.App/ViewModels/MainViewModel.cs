using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using TubeForge.App.Commands;
using TubeForge.Core.Diagnostics;
using TubeForge.Core.Errors;
using TubeForge.Core.Files;
using TubeForge.Core.Media;
using TubeForge.Core.Results;
using TubeForge.Core.Settings;
using TubeForge.Core.YouTube;
using TubeForge.Downloads;
using TubeForge.Downloads.History;
using TubeForge.Downloads.Queue;
using TubeForge.Media;
using TubeForge.Transcoding;
using TubeForge.Updates;
using TubeForge.YouTube;
using TubeForge.YouTube.Captions;
using TubeForge.YouTube.Collections;
using TubeForge.YouTube.Sidecars;

namespace TubeForge.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaximumConcurrentRequestsPerHost = 2;
    private static readonly Uri YouTubeOrigin = new("https://www.youtube.com/");

    private static readonly IReadOnlyList<DownloadModeOption> BaseModeChoices =
    [
        new(DownloadMode.AudioVideo, "Audio + video", "Best video + best audio, muxed locally"),
        new(DownloadMode.AudioOnly, "Audio only", "Native M4A/AAC or WebM/Opus audio"),
        new(DownloadMode.VideoOnly, "Video only", "Maximum video quality; audio not included")
    ];

    private static readonly IReadOnlyList<AudioProcessingOption> AudioProcessingChoices =
    [
        new(AudioOutputProfile.Native, "Native stream · no conversion", "Fastest; preserves source quality"),
        new(AudioOutputProfile.Mp3(320), "MP3 · 320 kbps", "Highest MP3 bitrate; largest file"),
        new(AudioOutputProfile.Mp3(256), "MP3 · 256 kbps", "High-quality MP3"),
        new(AudioOutputProfile.Mp3(192), "MP3 · 192 kbps", "Balanced quality and size"),
        new(AudioOutputProfile.Mp3(128), "MP3 · 128 kbps", "Smallest MP3 file")
    ];

    private static readonly IReadOnlyList<CaptionFormatOption> CaptionOutputChoices =
    [
        new(CaptionOutputFormat.SubRip, "SRT · broad player support", "srt"),
        new(CaptionOutputFormat.WebVtt, "WebVTT · native timed text", "vtt")
    ];

    private readonly HttpClient _httpClient;
    private readonly HttpClient _sidecarHttpClient;
    private readonly HttpClient _updateHttpClient;
    private readonly YouTubeMetadataResolver _resolver;
    private readonly DirectDownloadEngine _downloader;
    private readonly AdaptiveDownloadEngine _adaptiveDownloader;
    private readonly WindowsMediaFoundationTranscoder _audioTranscoder = new();
    private readonly CaptionDownloadEngine _captionDownloader;
    private readonly YouTubeCollectionResolver _collectionResolver;
    private readonly ThumbnailDownloadEngine _thumbnailDownloader;
    private readonly DownloadQueueStore _queueStore;
    private readonly DownloadHistoryStore _historyStore;
    private readonly TubeForgeSettingsStore _settingsStore;
    private readonly GitHubUpdateClient _updateClient;
    private readonly string _updateDirectory;
    private readonly DownloadQueueDispatcher _queueDispatcher = new(2);
    private readonly HostRequestGate _hostRequestGate;
    private readonly RateLimitedRequestExecutor _rateLimitedRequests;
    private readonly SemaphoreSlim _queueMutationLock = new(1, 1);
    private readonly SemaphoreSlim _historyMutationLock = new(1, 1);
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
    private AudioProcessingOption _selectedAudioProcessing = AudioProcessingChoices[0];
    private DownloadQueueSnapshot _queueSnapshot = new();
    private DownloadHistorySnapshot _historySnapshot = new();
    private bool _isInitialized;
    private bool _queueUnavailable;
    private bool _historyUnavailable;
    private string _downloadActionLabel = "Add to queue";
    private AppPage _activePage = AppPage.Download;
    private int _selectedMaxConcurrentDownloads = 2;
    private bool _enableSegmentedTransfers;
    private bool _enableAutomaticUpdateChecks = true;
    private string _fileNameTemplate = FileNameTemplate.Default;
    private TubeForgeSettings _settings;
    private bool _showResponsibleUseNotice = true;
    private string _settingsStatus = "Settings stay on this device.";
    private string _updateStatus = "TubeForge can check GitHub for verified stable releases.";
    private bool _isCheckingForUpdate;
    private bool _isDownloadingUpdate;
    private double _updateDownloadFraction;
    private UpdateRelease? _availableUpdate;
    private UpdateDownloadReceipt? _readyUpdate;
    private string _diagnosticsStatus = "Export contains whitelisted technical state only.";
    private string _diagnosticsExtractionStage = "NotRun";
    private IReadOnlyList<CaptionTrackOption> _captionTracks = [];
    private CaptionTrackOption? _selectedCaptionTrack;
    private CaptionFormatOption _selectedCaptionFormat = CaptionOutputChoices[0];
    private bool _isSavingCaption;
    private bool _isSavingSidecar;
    private YouTubeCollectionResult? _collection;

    public MainViewModel() : this(applicationDataDirectory: null)
    {
    }

    internal MainViewModel(string? applicationDataDirectory)
    {
        _hostRequestGate = new HostRequestGate(MaximumConcurrentRequestsPerHost);
        _rateLimitedRequests = new RateLimitedRequestExecutor(_hostRequestGate);
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
        _captionDownloader = new CaptionDownloadEngine(_httpClient);
        _collectionResolver = new YouTubeCollectionResolver(_httpClient);
        var sidecarHandler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _sidecarHttpClient = new HttpClient(sidecarHandler) { Timeout = TimeSpan.FromSeconds(60) };
        _thumbnailDownloader = new ThumbnailDownloadEngine(_sidecarHttpClient);
        var updateHandler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        };
        _updateHttpClient = new HttpClient(updateHandler) { Timeout = TimeSpan.FromSeconds(60) };
        _updateClient = new GitHubUpdateClient(_updateHttpClient);
        applicationDataDirectory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubeForge");
        applicationDataDirectory = Path.GetFullPath(applicationDataDirectory);
        _queueStore = new DownloadQueueStore(Path.Combine(applicationDataDirectory, "queue.json"));
        _historyStore = new DownloadHistoryStore(Path.Combine(applicationDataDirectory, "history.json"));
        _settingsStore = new TubeForgeSettingsStore(Path.Combine(applicationDataDirectory, "settings.json"));
        _updateDirectory = Path.Combine(applicationDataDirectory, "updates");
        _downloadFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
        _settings = new TubeForgeSettings { DownloadFolder = Path.GetFullPath(_downloadFolder) };

        AnalyzeCommand = new AsyncRelayCommand(
            AnalyzeAsync,
            () => !ShowResponsibleUseNotice && !IsAnalyzing && !string.IsNullOrWhiteSpace(UrlText));
        DownloadCommand = new AsyncRelayCommand(
            DownloadAsync,
            () => !ShowResponsibleUseNotice && !IsAnalyzing && SelectedFormat is not null && HasVideo);
        DownloadCaptionCommand = new AsyncRelayCommand(
            DownloadCaptionAsync,
            () => !ShowResponsibleUseNotice && !IsAnalyzing && !IsSavingCaption && SelectedCaptionTrack is not null);
        SaveThumbnailCommand = new AsyncRelayCommand(
            SaveThumbnailAsync,
            () => !ShowResponsibleUseNotice && !IsAnalyzing && !IsSavingSidecar && _metadata?.ThumbnailUrl is not null);
        SaveMetadataCommand = new AsyncRelayCommand(
            SaveMetadataAsync,
            () => !ShowResponsibleUseNotice && !IsAnalyzing && !IsSavingSidecar && _metadata is not null);
        SelectAllCollectionCommand = new RelayCommand(
            () => SetCollectionSelection(isSelected: true),
            () => !IsAnalyzing && CollectionItems.Any(item => !item.IsSelected));
        SelectNoneCollectionCommand = new RelayCommand(
            () => SetCollectionSelection(isSelected: false),
            () => !IsAnalyzing && CollectionItems.Any(item => item.IsSelected));
        QueueCollectionCommand = new AsyncRelayCommand(
            QueueSelectedCollectionAsync,
            () => !ShowResponsibleUseNotice && !IsAnalyzing && CollectionItems.Any(item => item.IsSelected));
        CancelAnalysisCommand = new RelayCommand(CancelAnalysis, () => IsAnalyzing);
        CancelCommand = new RelayCommand(PauseAll, () => IsDownloading);
        ShowDownloadCommand = new RelayCommand(() => ShowPage(AppPage.Download));
        ShowQueueCommand = new RelayCommand(() => ShowPage(AppPage.Queue));
        ShowLibraryCommand = new RelayCommand(() => ShowPage(AppPage.Library));
        ShowSettingsCommand = new RelayCommand(() => ShowPage(AppPage.Settings));
        ShowDiagnosticsCommand = new RelayCommand(() => ShowPage(AppPage.Diagnostics));
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync);
        AcceptResponsibleUseCommand = new AsyncRelayCommand(AcceptResponsibleUseAsync);
        CheckForUpdatesCommand = new AsyncRelayCommand(
            () => CheckForUpdatesAsync(isAutomatic: false),
            () => !ShowResponsibleUseNotice && !IsCheckingForUpdate && !IsDownloadingUpdate);
        DownloadUpdateCommand = new AsyncRelayCommand(
            DownloadUpdateAsync,
            () => _availableUpdate is not null && !IsCheckingForUpdate && !IsDownloadingUpdate);
        ClearCompletedCommand = new AsyncRelayCommand(ClearCompletedAsync, () => QueueItems.Any(item => item.Status == DownloadQueueStatus.Completed));
        ClearHistoryCommand = new AsyncRelayCommand(ClearHistoryAsync, () => HistoryItems.Count > 0 || _historyUnavailable);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FormatItemViewModel> Formats { get; } = [];

    public ObservableCollection<QueueItemViewModel> QueueItems { get; } = [];

    public ObservableCollection<CollectionItemViewModel> CollectionItems { get; } = [];

    public ObservableCollection<HistoryItemViewModel> HistoryItems { get; } = [];

    public IReadOnlyList<DownloadModeOption> DownloadModes => _downloadModes;

    public IReadOnlyList<AudioProcessingOption> AudioProcessingOptions => AudioProcessingChoices;

    public AsyncRelayCommand AnalyzeCommand { get; }

    public AsyncRelayCommand DownloadCommand { get; }

    public AsyncRelayCommand DownloadCaptionCommand { get; }

    public AsyncRelayCommand SaveThumbnailCommand { get; }

    public AsyncRelayCommand SaveMetadataCommand { get; }

    public RelayCommand SelectAllCollectionCommand { get; }

    public RelayCommand SelectNoneCollectionCommand { get; }

    public AsyncRelayCommand QueueCollectionCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand CancelAnalysisCommand { get; }

    public RelayCommand ShowDownloadCommand { get; }

    public RelayCommand ShowQueueCommand { get; }

    public RelayCommand ShowLibraryCommand { get; }

    public RelayCommand ShowSettingsCommand { get; }

    public RelayCommand ShowDiagnosticsCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand AcceptResponsibleUseCommand { get; }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    public AsyncRelayCommand DownloadUpdateCommand { get; }

    public AsyncRelayCommand ClearCompletedCommand { get; }

    public AsyncRelayCommand ClearHistoryCommand { get; }

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
        _videoDuration is null ? string.Empty : FormatDuration(_videoDuration.Value),
        _metadata?.ContentKind switch
        {
            VideoContentKind.Short => "Short",
            VideoContentKind.LiveReplay => "Completed live replay",
            _ => string.Empty
        },
        _metadata?.Chapters.Count > 0 ? $"{_metadata.Chapters.Count} chapters" : string.Empty
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

    public bool HasCollection => _collection is not null;

    public string CollectionTitle => _collection?.Title ?? string.Empty;

    public string CollectionSummary => _collection is null
        ? string.Empty
        : $"{CollectionItems.Count} videos · {_collection.PagesRead} pages" +
          (_collection.IsTruncated ? " · bounded result" : string.Empty);

    public string CollectionSelectionSummary =>
        $"{CollectionItems.Count(item => item.IsSelected)} of {CollectionItems.Count} selected";

    public IReadOnlyList<CaptionTrackOption> CaptionTracks
    {
        get => _captionTracks;
        private set
        {
            if (Set(ref _captionTracks, value))
            {
                OnPropertyChanged(nameof(HasCaptions));
            }
        }
    }

    public bool HasCaptions => CaptionTracks.Count > 0;

    public CaptionTrackOption? SelectedCaptionTrack
    {
        get => _selectedCaptionTrack;
        set
        {
            if (Set(ref _selectedCaptionTrack, value))
            {
                RefreshCommands();
            }
        }
    }

    public IReadOnlyList<CaptionFormatOption> CaptionFormats => CaptionOutputChoices;

    public CaptionFormatOption SelectedCaptionFormat
    {
        get => _selectedCaptionFormat;
        set
        {
            if (value is not null)
            {
                Set(ref _selectedCaptionFormat, value);
            }
        }
    }

    public bool IsSavingCaption
    {
        get => _isSavingCaption;
        private set
        {
            if (Set(ref _isSavingCaption, value))
            {
                RefreshCommands();
            }
        }
    }

    public bool IsSavingSidecar
    {
        get => _isSavingSidecar;
        private set
        {
            if (Set(ref _isSavingSidecar, value))
            {
                RefreshCommands();
            }
        }
    }

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

    public bool IsDownloadPage => _activePage == AppPage.Download;

    public bool IsQueuePage => _activePage == AppPage.Queue;

    public bool IsLibraryPage => _activePage == AppPage.Library;

    public bool IsSettingsPage => _activePage == AppPage.Settings;

    public bool IsDiagnosticsPage => _activePage == AppPage.Diagnostics;

    public bool ShowResponsibleUseNotice
    {
        get => _showResponsibleUseNotice;
        private set
        {
            if (Set(ref _showResponsibleUseNotice, value))
            {
                OnPropertyChanged(nameof(IsMainContentEnabled));
                RefreshCommands();
            }
        }
    }

    public bool IsMainContentEnabled => !ShowResponsibleUseNotice;

    public string SettingsStatus
    {
        get => _settingsStatus;
        private set => Set(ref _settingsStatus, value);
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => Set(ref _updateStatus, value);
    }

    public bool IsCheckingForUpdate
    {
        get => _isCheckingForUpdate;
        private set
        {
            if (Set(ref _isCheckingForUpdate, value)) RefreshCommands();
        }
    }

    public bool IsDownloadingUpdate
    {
        get => _isDownloadingUpdate;
        private set
        {
            if (Set(ref _isDownloadingUpdate, value)) RefreshCommands();
        }
    }

    public double UpdateDownloadFraction
    {
        get => _updateDownloadFraction;
        private set => Set(ref _updateDownloadFraction, value);
    }

    public bool HasUpdateAvailable => _availableUpdate is not null && _readyUpdate is null;

    public bool IsUpdateReady => _readyUpdate is not null;

    public string AvailableUpdateVersion => _availableUpdate?.Version.ToString(3) ?? string.Empty;

    public string DiagnosticsStatus
    {
        get => _diagnosticsStatus;
        private set => Set(ref _diagnosticsStatus, value);
    }

    public string ApplicationVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "development";

    public string RuntimeDescription => RuntimeInformation.FrameworkDescription;

    public string ProcessArchitecture => RuntimeInformation.ProcessArchitecture.ToString();

    public string DependencyStatus => "No third-party packages or bundled executables";

    public string QueueStoragePath => _queueStore.StoragePath;

    public string HistoryStoragePath => _historyStore.StoragePath;

    public string SettingsStoragePath => _settingsStore.StoragePath;

    public string DiagnosticsExtractionStatus => _diagnosticsExtractionStage;

    public string DiagnosticsFormatSummary => _metadata is null
        ? "No active media metadata"
        : $"{_allFormats.Count} direct formats · {Formats.Count} matching outputs";

    public bool HasQueueItems => QueueItems.Count > 0;

    public bool HasHistory => HistoryItems.Count > 0;

    public string HistorySummary => HistoryItems.Count == 1
        ? "1 completed output"
        : $"{HistoryItems.Count} completed outputs";

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

    public bool EnableSegmentedTransfers
    {
        get => _enableSegmentedTransfers;
        set => Set(ref _enableSegmentedTransfers, value);
    }

    public bool EnableAutomaticUpdateChecks
    {
        get => _enableAutomaticUpdateChecks;
        set => Set(ref _enableAutomaticUpdateChecks, value);
    }

    public string FileNameTemplateText
    {
        get => _fileNameTemplate;
        set => Set(ref _fileNameTemplate, value);
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

    public AudioProcessingOption SelectedAudioProcessing
    {
        get => _selectedAudioProcessing;
        set
        {
            if (value is not null && Set(ref _selectedAudioProcessing, value))
            {
                OnPropertyChanged(nameof(ModeNotice));
            }
        }
    }

    public bool HasVideoFilters => SelectedDownloadMode.Value is not DownloadMode.AudioOnly;

    public bool IsAudioOnly => SelectedDownloadMode.Value is DownloadMode.AudioOnly;

    public bool IsAudioVideo => SelectedDownloadMode.Value is DownloadMode.AudioVideo;

    public string ModeNotice => SelectedDownloadMode.Value switch
    {
        DownloadMode.AudioVideo =>
            CombinedModeNotice(),
        DownloadMode.AudioOnly =>
            SelectedAudioProcessing.Value.Kind == AudioOutputKind.Native
                ? "Native AAC/Opus: fastest path with no re-encoding or quality loss."
                : $"MP3 {SelectedAudioProcessing.Value.BitrateKbps} kbps: Windows Media Foundation decodes and re-encodes locally; no FFmpeg.",
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
        private set
        {
            if (Set(ref _extractionStatus, value))
            {
                OnPropertyChanged(nameof(DiagnosticsExtractionStatus));
            }
        }
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        var settingsResult = await _settingsStore.LoadAsync(_settings);
        if (settingsResult.IsSuccess)
        {
            _settings = settingsResult.Value;
            _downloadFolder = _settings.DownloadFolder;
            OnPropertyChanged(nameof(DownloadFolder));
            _selectedMaxConcurrentDownloads = _settings.MaximumConcurrentDownloads;
            _queueDispatcher.MaximumConcurrency = _selectedMaxConcurrentDownloads;
            OnPropertyChanged(nameof(SelectedMaxConcurrentDownloads));
            _enableSegmentedTransfers = _settings.EnableSegmentedTransfers;
            OnPropertyChanged(nameof(EnableSegmentedTransfers));
            _enableAutomaticUpdateChecks = _settings.EnableAutomaticUpdateChecks;
            OnPropertyChanged(nameof(EnableAutomaticUpdateChecks));
            _fileNameTemplate = _settings.FileNameTemplate;
            OnPropertyChanged(nameof(FileNameTemplateText));
            ShowResponsibleUseNotice = !_settings.ResponsibleUseAccepted;
        }
        else
        {
            ErrorMessage = $"{settingsResult.Error!.Message} ({settingsResult.Error.Code})";
            SettingsStatus = "Settings unavailable; defaults remain active.";
            ShowResponsibleUseNotice = true;
        }

        var queueResult = await _queueStore.LoadAsync();
        if (!queueResult.IsSuccess)
        {
            _queueUnavailable = true;
            ErrorMessage = $"{queueResult.Error!.Message} ({queueResult.Error.Code})";
            StatusMessage = "Queue recovery unavailable";
        }
        else
        {
            _queueSnapshot = queueResult.Value;
            RebuildQueueItems();
            var recoverableCount = _queueSnapshot.Items.Count(item =>
                item.Status is DownloadQueueStatus.Queued or DownloadQueueStatus.Paused);
            if (recoverableCount > 0)
            {
                StatusMessage = recoverableCount == 1
                    ? "Recovered 1 interrupted download"
                    : $"Recovered {recoverableCount} interrupted downloads";
            }
        }

        var historyResult = await _historyStore.LoadAsync();
        if (!historyResult.IsSuccess)
        {
            _historyUnavailable = true;
            ErrorMessage = $"{historyResult.Error!.Message} ({historyResult.Error.Code})";
            NotifyHistoryProperties();
        }
        else
        {
            _historySnapshot = historyResult.Value;
            RebuildHistoryItems();
        }

        if (!_queueUnavailable)
        {
            PumpQueue();
        }

        if (!ShowResponsibleUseNotice && EnableAutomaticUpdateChecks)
        {
            _ = CheckForUpdatesAsync(isAutomatic: true);
        }
    }

    private async Task SaveSettingsAsync()
    {
        if (!FileNameTemplate.IsValid(FileNameTemplateText))
        {
            ErrorMessage = "The filename template contains an unknown token or unmatched brace. (FileName.InvalidTemplate)";
            SettingsStatus = "Filename template was not saved.";
            return;
        }

        TubeForgeSettings next;
        try
        {
            if (!Path.IsPathFullyQualified(DownloadFolder))
            {
                throw new ArgumentException("Download folder must be an absolute path.");
            }

            next = _settings with
            {
                DownloadFolder = Path.GetFullPath(DownloadFolder),
                MaximumConcurrentDownloads = SelectedMaxConcurrentDownloads,
                FileNameTemplate = FileNameTemplateText,
                EnableSegmentedTransfers = EnableSegmentedTransfers,
                EnableAutomaticUpdateChecks = EnableAutomaticUpdateChecks
            };
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ErrorMessage = "The download folder setting is invalid. (Settings.InvalidState)";
            SettingsStatus = exception.GetType().Name;
            return;
        }

        var result = await _settingsStore.SaveAsync(next);
        if (!result.IsSuccess)
        {
            ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
            SettingsStatus = "Settings were not saved.";
            return;
        }

        _settings = next;
        ErrorMessage = string.Empty;
        SettingsStatus = "Settings saved locally.";
    }

    private async Task AcceptResponsibleUseAsync()
    {
        if (!FileNameTemplate.IsValid(FileNameTemplateText))
        {
            ErrorMessage = "The filename template contains an unknown token or unmatched brace. (FileName.InvalidTemplate)";
            return;
        }

        TubeForgeSettings accepted;
        try
        {
            if (!Path.IsPathFullyQualified(DownloadFolder))
            {
                throw new ArgumentException("Download folder must be an absolute path.");
            }

            accepted = _settings with
            {
                DownloadFolder = Path.GetFullPath(DownloadFolder),
                MaximumConcurrentDownloads = SelectedMaxConcurrentDownloads,
                FileNameTemplate = FileNameTemplateText,
                EnableSegmentedTransfers = EnableSegmentedTransfers,
                EnableAutomaticUpdateChecks = EnableAutomaticUpdateChecks,
                ResponsibleUseAccepted = true
            };
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ErrorMessage = "The download folder setting is invalid. (Settings.InvalidState)";
            SettingsStatus = exception.GetType().Name;
            return;
        }

        var result = await _settingsStore.SaveAsync(accepted);
        if (!result.IsSuccess)
        {
            ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
            return;
        }

        _settings = accepted;
        ShowResponsibleUseNotice = false;
        SettingsStatus = "Responsible-use acknowledgement saved locally.";
        if (EnableAutomaticUpdateChecks)
        {
            _ = CheckForUpdatesAsync(isAutomatic: true);
        }
    }

    private void ShowPage(AppPage page)
    {
        if (_activePage == page)
        {
            return;
        }

        _activePage = page;
        OnPropertyChanged(nameof(IsDownloadPage));
        OnPropertyChanged(nameof(IsQueuePage));
        OnPropertyChanged(nameof(IsLibraryPage));
        OnPropertyChanged(nameof(IsSettingsPage));
        OnPropertyChanged(nameof(IsDiagnosticsPage));
    }

    public async Task AnalyzeAsync()
    {
        CancelAnalysis();
        ClearAnalysis();
        var collectionReference = YouTubeCollectionUrlParser.Parse(UrlText);
        if (collectionReference.IsSuccess)
        {
            await AnalyzeCollectionAsync(collectionReference.Value);
            return;
        }

        var parsed = YouTubeUrlParser.ParseVideoId(UrlText);
        if (!parsed.IsSuccess)
        {
            _diagnosticsExtractionStage = "InputRejected";
            OnPropertyChanged(nameof(DiagnosticsExtractionStatus));
            ErrorMessage = $"{parsed.Error!.Message} ({parsed.Error.Code})";
            StatusMessage = "URL rejected";
            return;
        }

        _analysisCancellation = new CancellationTokenSource();
        IsAnalyzing = true;
        StatusMessage = "Analyzing video…";
        try
        {
            var result = await _rateLimitedRequests.ExecuteAsync(
                YouTubeOrigin,
                token => _resolver.ResolveAsync(parsed.Value, token),
                delay => StatusMessage = $"Rate limited · retrying in {FormatRateLimitDelay(delay)}",
                _analysisCancellation.Token);
            if (!result.IsSuccess)
            {
                _diagnosticsExtractionStage = "ResolutionFailed";
                OnPropertyChanged(nameof(DiagnosticsExtractionStatus));
                ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
                StatusMessage = "Analysis failed";
                return;
            }

            _metadata = result.Value.Metadata;
            CaptionTracks = _metadata.CaptionTracks.Select(track => new CaptionTrackOption(track)).ToArray();
            SelectedCaptionTrack = CaptionTracks.FirstOrDefault(track => !track.Track.IsAutoGenerated) ??
                                   CaptionTracks.FirstOrDefault();
            _diagnosticsExtractionStage = result.Value.Diagnostics?.Stage ?? "WatchPageResolved";
            OnPropertyChanged(nameof(DiagnosticsExtractionStatus));
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

            var extractionStatus = result.Value.Diagnostics?.Stage == "AndroidClientResolved"
                ? "DIRECT STREAMS VERIFIED"
                : "WATCH PAGE RESOLVED";
            ExtractionStatus = _metadata.ContentKind switch
            {
                VideoContentKind.Short => "SHORT · " + extractionStatus,
                VideoContentKind.LiveReplay => "LIVE REPLAY · " + extractionStatus,
                _ => extractionStatus
            };
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

    private async Task AnalyzeCollectionAsync(YouTubeCollectionReference source)
    {
        _analysisCancellation = new CancellationTokenSource();
        IsAnalyzing = true;
        StatusMessage = source.Kind == YouTubeCollectionKind.Playlist
            ? "Enumerating playlist…"
            : "Enumerating channel videos…";
        try
        {
            var result = await _rateLimitedRequests.ExecuteAsync(
                YouTubeOrigin,
                token => _collectionResolver.ResolveAsync(source, maximumItems: 1_000, token),
                delay => StatusMessage = $"Rate limited · retrying in {FormatRateLimitDelay(delay)}",
                _analysisCancellation.Token);
            if (!result.IsSuccess)
            {
                _diagnosticsExtractionStage = "CollectionResolutionFailed";
                OnPropertyChanged(nameof(DiagnosticsExtractionStatus));
                ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
                StatusMessage = "Collection analysis failed";
                return;
            }

            _collection = result.Value;
            foreach (var item in _collection.Items)
            {
                CollectionItems.Add(new CollectionItemViewModel(item, CollectionSelectionChanged));
            }

            _diagnosticsExtractionStage = "CollectionResolved";
            OnPropertyChanged(nameof(DiagnosticsExtractionStatus));
            OnPropertyChanged(nameof(HasCollection));
            OnPropertyChanged(nameof(CollectionTitle));
            OnPropertyChanged(nameof(CollectionSummary));
            OnPropertyChanged(nameof(CollectionSelectionSummary));
            StatusMessage = CollectionItems.Count > 0
                ? "Choose collection items to queue"
                : "Collection contains no downloadable video entries";
            ProgressDetail = CollectionSummary;
            RefreshCommands();
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
        var output = AudioOutputFor(selection);
        try
        {
            var renderedName = RenderFileName(_metadata, selection, index: null, indexWidth: 2, output);
            if (!renderedName.IsSuccess)
            {
                ErrorMessage = $"{renderedName.Error!.Message} ({renderedName.Error.Code})";
                ProgressDetail = renderedName.Error.TechnicalDetail ?? string.Empty;
                StatusMessage = "Download not queued";
                return;
            }

            var destination = FileNamePolicy.AvailablePath(
                DownloadFolder,
                renderedName.Value,
                OutputExtension(selection, output),
                path => File.Exists(path) ||
                        File.Exists(path + ".part") ||
                        _queueSnapshot.Items.Any(item => item.DestinationPath.Equals(path, StringComparison.OrdinalIgnoreCase)) ||
                        _historySnapshot.Entries.Any(item => item.DestinationPath.Equals(path, StringComparison.OrdinalIgnoreCase)));
            var duplicate = DownloadDuplicateDetector.Find(
                SelectionIdentity(_metadata, selection, output),
                destination,
                _queueSnapshot.Items,
                _historySnapshot.Entries);
            if (duplicate is not null)
            {
                ErrorMessage = $"This exact output is already queued or recorded in Library. Remove that record to download it again. (Queue.DuplicateOutput)";
                ProgressDetail = DuplicateDescription(duplicate.Kind);
                StatusMessage = "Duplicate not queued";
                return;
            }

            var queueItem = CreateQueueItem(_metadata, selection, destination, output);
            var queueError = await UpsertQueueItemAsync(queueItem);
            if (queueError is not null)
            {
                ErrorMessage = $"{queueError.Message} ({queueError.Code})";
                StatusMessage = "Download not queued";
                return;
            }

            _preparedQueueWork[queueItem.Id] = new QueuedDownloadWork(_metadata, selection, destination, output);
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

    private async Task QueueSelectedCollectionAsync()
    {
        if (_collection is null)
        {
            return;
        }

        var selected = CollectionItems.Where(item => item.IsSelected).ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        await InitializeAsync();
        CancelAnalysis();
        _analysisCancellation = new CancellationTokenSource();
        var cancellationToken = _analysisCancellation.Token;
        IsAnalyzing = true;
        ErrorMessage = string.Empty;
        var queued = 0;
        var skipped = 0;
        var failed = 0;
        var deferred = 0;
        var stoppedForRateLimit = false;
        var indexWidth = Math.Max(2, CollectionItems.Count.ToString().Length);
        try
        {
            for (var itemIndex = 0; itemIndex < selected.Length; itemIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var card = selected[itemIndex];
                StatusMessage = $"Preparing collection item {itemIndex + 1} of {selected.Length}…";
                card.SetStatus("Resolving streams…");
                var resolved = await _rateLimitedRequests.ExecuteAsync(
                    YouTubeOrigin,
                    token => _resolver.ResolveAsync(card.Item.VideoId, token),
                    delay => card.SetStatus($"Rate limited · retrying in {FormatRateLimitDelay(delay)}"),
                    cancellationToken);
                if (!resolved.IsSuccess)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        card.SetStatus("Cancelled");
                        break;
                    }

                    card.SetStatus($"Failed · {resolved.Error!.Code}");
                    failed++;
                    if (resolved.Error.Code == "Network.RateLimited")
                    {
                        stoppedForRateLimit = true;
                        foreach (var remaining in selected.Skip(itemIndex + 1))
                        {
                            remaining.SetStatus("Deferred · rate limited");
                            deferred++;
                        }

                        break;
                    }

                    continue;
                }

                var metadata = resolved.Value.Metadata;
                var selection = BestCompleteFileSelection(metadata.Formats);
                if (selection is null)
                {
                    card.SetStatus("Failed · no complete output");
                    failed++;
                    continue;
                }

                try
                {
                    var position = card.Item.Index ?? itemIndex + 1;
                    var renderedName = RenderFileName(metadata, selection, position, indexWidth);
                    if (!renderedName.IsSuccess)
                    {
                        card.SetStatus($"Failed · {renderedName.Error!.Code}");
                        failed++;
                        continue;
                    }

                    var destination = FileNamePolicy.AvailablePath(
                        DownloadFolder,
                        renderedName.Value,
                        selection.RequiresMuxing
                            ? FormatDisplay.Extension(selection.Format.Container)
                            : FormatDisplay.OutputExtension(selection.Format),
                        path => File.Exists(path) ||
                                File.Exists(path + ".part") ||
                                _queueSnapshot.Items.Any(existing => existing.DestinationPath.Equals(
                                    path,
                                    StringComparison.OrdinalIgnoreCase)) ||
                                _historySnapshot.Entries.Any(existing => existing.DestinationPath.Equals(
                                    path,
                                    StringComparison.OrdinalIgnoreCase)));
                    var duplicate = DownloadDuplicateDetector.Find(
                        SelectionIdentity(metadata, selection),
                        destination,
                        _queueSnapshot.Items,
                        _historySnapshot.Entries);
                    if (duplicate is not null)
                    {
                        card.SetStatus($"Skipped · {DuplicateDescription(duplicate.Kind)}");
                        skipped++;
                        continue;
                    }

                    var queueItem = CreateQueueItem(metadata, selection, destination);
                    var queueError = await UpsertQueueItemAsync(queueItem, cancellationToken);
                    if (queueError is not null)
                    {
                        card.SetStatus($"Failed · {queueError.Code}");
                        failed++;
                        continue;
                    }

                    card.SetStatus($"Queued · {Path.GetFileName(destination)}");
                    queued++;
                    PumpQueue();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                                  ArgumentException or NotSupportedException)
                {
                    card.SetStatus($"Failed · {exception.GetType().Name}");
                    failed++;
                }
            }

            StatusMessage = cancellationToken.IsCancellationRequested
                ? "Collection queue preparation cancelled"
                : stoppedForRateLimit
                    ? "YouTube rate limited collection preparation"
                    : $"Queued {queued} collection items";
            ProgressDetail = $"{queued} queued · {skipped} skipped · {failed} failed · {deferred} deferred";
            if (failed > 0)
            {
                ErrorMessage = stoppedForRateLimit
                    ? "TubeForge stopped bulk requests after repeated rate limits. Retry deferred items later. (Network.RateLimited)"
                    : "Some collection items could not be prepared. Review item statuses. (Collection.PartialFailure)";
            }
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    private static FormatItemViewModel? BestCompleteFileSelection(IReadOnlyList<StreamFormat> formats)
    {
        var audio = formats
            .Where(format => format.Kind == StreamKind.AudioOnly)
            .OrderByDescending(format => format.Bitrate ?? 0)
            .ThenByDescending(format => format.AudioSampleRate ?? 0)
            .ThenBy(format => format.FormatId)
            .ToArray();
        var adaptiveVideos = formats
            .Where(format => format.Kind == StreamKind.VideoOnly &&
                             audio.Any(candidate => AdaptiveFormatSelector.AreMuxCompatible(format, candidate)))
            .OrderByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.FramesPerSecond ?? 0)
            .ThenByDescending(format => format.IsHdr)
            .ThenByDescending(format => format.Container == MediaContainer.Mp4)
            .ThenByDescending(format => format.Bitrate ?? 0)
            .ThenBy(format => format.FormatId);
        foreach (var video in adaptiveVideos)
        {
            var companion = audio.First(candidate => AdaptiveFormatSelector.AreMuxCompatible(video, candidate));
            return new FormatItemViewModel(video, companion);
        }

        var progressive = formats
            .Where(format => format.Kind == StreamKind.Progressive)
            .OrderByDescending(format => format.Height ?? 0)
            .ThenByDescending(format => format.FramesPerSecond ?? 0)
            .ThenByDescending(format => format.IsHdr)
            .ThenByDescending(format => format.Container == MediaContainer.Mp4)
            .ThenByDescending(format => format.Bitrate ?? 0)
            .ThenBy(format => format.FormatId)
            .FirstOrDefault();
        return progressive is null ? null : new FormatItemViewModel(progressive);
    }

    private async Task DownloadCaptionAsync()
    {
        if (_metadata is null || SelectedCaptionTrack is null)
        {
            return;
        }

        IsSavingCaption = true;
        ErrorMessage = string.Empty;
        try
        {
            var track = SelectedCaptionTrack.Track;
            var kind = track.IsAutoGenerated ? " auto" : string.Empty;
            var destination = FileNamePolicy.AvailablePath(
                DownloadFolder,
                $"{_metadata.Title} [{track.LanguageCode}{kind}]",
                SelectedCaptionFormat.Extension,
                path => File.Exists(path) || File.Exists(path + ".part"));
            var result = await _captionDownloader.DownloadAsync(new CaptionDownloadRequest
            {
                SourceUrl = track.Url,
                DestinationPath = destination,
                OutputFormat = SelectedCaptionFormat.Value
            });
            if (!result.IsSuccess)
            {
                ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
                StatusMessage = "Caption save failed";
                return;
            }

            StatusMessage = $"Saved caption: {Path.GetFileName(result.Value.DestinationPath)}";
            ProgressDetail = $"{result.Value.CueCount} cues · {SelectedCaptionFormat.Label}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            ErrorMessage = "The caption destination is invalid or unavailable. (Caption.InvalidDestination)";
            ProgressDetail = exception.GetType().Name;
            StatusMessage = "Caption save failed";
        }
        finally
        {
            IsSavingCaption = false;
        }
    }

    private async Task SaveThumbnailAsync()
    {
        if (_metadata?.ThumbnailUrl is not { } thumbnailUrl)
        {
            return;
        }

        IsSavingSidecar = true;
        ErrorMessage = string.Empty;
        try
        {
            var extension = ThumbnailDownloadEngine.FileExtensionFor(thumbnailUrl);
            var destination = FileNamePolicy.AvailablePath(
                DownloadFolder,
                $"{_metadata.Title}.thumbnail",
                extension,
                path => File.Exists(path) || File.Exists(path + ".part"));
            var result = await _thumbnailDownloader.DownloadAsync(new ThumbnailDownloadRequest
            {
                SourceUrl = thumbnailUrl,
                DestinationPath = destination
            });
            if (!result.IsSuccess)
            {
                ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
                StatusMessage = "Thumbnail save failed";
                return;
            }

            StatusMessage = $"Saved thumbnail: {Path.GetFileName(result.Value.DestinationPath)}";
            ProgressDetail = $"{result.Value.MediaType} · {FormatBytes(result.Value.BytesWritten)}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            ErrorMessage = "The thumbnail destination is invalid or unavailable. (Thumbnail.InvalidDestination)";
            ProgressDetail = exception.GetType().Name;
            StatusMessage = "Thumbnail save failed";
        }
        finally
        {
            IsSavingSidecar = false;
        }
    }

    private async Task SaveMetadataAsync()
    {
        if (_metadata is null)
        {
            return;
        }

        IsSavingSidecar = true;
        ErrorMessage = string.Empty;
        try
        {
            var destination = FileNamePolicy.AvailablePath(
                DownloadFolder,
                $"{_metadata.Title}.info",
                "json",
                path => File.Exists(path) || File.Exists(path + ".part"));
            var result = await MetadataSidecarWriter.WriteAsync(_metadata, destination);
            if (!result.IsSuccess)
            {
                ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
                StatusMessage = "Metadata save failed";
                return;
            }

            StatusMessage = $"Saved metadata: {Path.GetFileName(result.Value.DestinationPath)}";
            ProgressDetail = $"JSON · {FormatBytes(result.Value.BytesWritten)} · stream URLs excluded";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          ArgumentException or NotSupportedException)
        {
            ErrorMessage = "The metadata destination is invalid or unavailable. (Sidecar.InvalidDestination)";
            ProgressDetail = exception.GetType().Name;
            StatusMessage = "Metadata save failed";
        }
        finally
        {
            IsSavingSidecar = false;
        }
    }

    public void Cancel() => PauseAll();

    public string BuildDiagnosticReport() => RedactedDiagnosticReportBuilder.Build(new DiagnosticReportInput
    {
        GeneratedAtUtc = DateTimeOffset.UtcNow,
        ApplicationVersion = ApplicationVersion,
        RuntimeDescription = RuntimeDescription,
        ProcessArchitecture = ProcessArchitecture,
        ExtractionStage = DiagnosticsExtractionStatus,
        TotalFormats = _allFormats.Count,
        MatchingOutputs = Formats.Count,
        ActiveQueueItems = _queueSnapshot.Items.Count(item => item.Status == DownloadQueueStatus.Downloading),
        WaitingQueueItems = _queueSnapshot.Items.Count(item => item.Status == DownloadQueueStatus.Queued),
        PausedQueueItems = _queueSnapshot.Items.Count(item => item.Status == DownloadQueueStatus.Paused),
        CompletedQueueItems = _queueSnapshot.Items.Count(item => item.Status == DownloadQueueStatus.Completed),
        FailedQueueItems = _queueSnapshot.Items.Count(item => item.Status == DownloadQueueStatus.Failed),
        CancelledQueueItems = _queueSnapshot.Items.Count(item => item.Status == DownloadQueueStatus.Cancelled)
    });

    public void SetDiagnosticsStatus(string status) => DiagnosticsStatus = status;

    public async Task<bool> StartReadyUpdateAsync()
    {
        var ready = _readyUpdate;
        if (ready is null)
        {
            UpdateStatus = "Download and verify an update before installing it.";
            return false;
        }

        try
        {
            var installerPath = Path.GetFullPath(ready.InstallerPath);
            var expectedDirectory = Path.GetFullPath(_updateDirectory);
            if (!string.Equals(
                    Path.GetDirectoryName(installerPath),
                    expectedDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Update installer escaped its staging directory.");
            }

            var info = new FileInfo(installerPath);
            if (!info.Exists || info.Length != ready.BytesWritten)
            {
                throw new IOException("Update installer size changed after verification.");
            }

            await using var stream = new FileStream(
                installerPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            if (!hash.Equals(ready.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Update installer hash changed after verification.");
            }

            var start = new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = false
            };
            start.ArgumentList.Add("/update");
            start.ArgumentList.Add("/wait-pid");
            start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add("/launch");
            _ = Process.Start(start) ?? throw new IOException("The verified update installer did not start.");
            UpdateStatus = $"Installing TubeForge {ready.Version.ToString(3)}…";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            UpdateStatus = $"Update install failed safely ({exception.GetType().Name}).";
            return false;
        }
    }

    private async Task CheckForUpdatesAsync(bool isAutomatic)
    {
        IsCheckingForUpdate = true;
        UpdateStatus = "Checking GitHub for a verified stable release…";
        try
        {
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
            current = new Version(current.Major, current.Minor, Math.Max(0, current.Build));
            var result = await _updateClient.CheckForUpdateAsync(current);
            if (!result.IsSuccess)
            {
                UpdateStatus = isAutomatic
                    ? "Automatic update check unavailable; use Check now to retry."
                    : $"Update check failed safely ({result.Error!.Code}).";
                return;
            }

            _availableUpdate = result.Value;
            _readyUpdate = null;
            UpdateDownloadFraction = 0;
            UpdateStatus = _availableUpdate is null
                ? $"TubeForge {current.ToString(3)} is up to date."
                : $"TubeForge {_availableUpdate.Version.ToString(3)} is available and ready to download.";
            NotifyUpdateProperties();
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    private async Task DownloadUpdateAsync()
    {
        var release = _availableUpdate;
        if (release is null)
        {
            return;
        }

        IsDownloadingUpdate = true;
        UpdateDownloadFraction = 0;
        UpdateStatus = $"Downloading and verifying TubeForge {release.Version.ToString(3)}…";
        try
        {
            var progress = new Progress<double>(value => UpdateDownloadFraction = value);
            var result = await _updateClient.DownloadInstallerAsync(release, _updateDirectory, progress);
            if (!result.IsSuccess)
            {
                UpdateStatus = $"Update download rejected safely ({result.Error!.Code}).";
                return;
            }

            _readyUpdate = result.Value;
            UpdateStatus = $"TubeForge {release.Version.ToString(3)} verified. Install when ready.";
            NotifyUpdateProperties();
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    private void NotifyUpdateProperties()
    {
        OnPropertyChanged(nameof(HasUpdateAvailable));
        OnPropertyChanged(nameof(IsUpdateReady));
        OnPropertyChanged(nameof(AvailableUpdateVersion));
        RefreshCommands();
    }

    public void Dispose()
    {
        CancelAnalysis();
        PauseAll();
        _updateHttpClient.Dispose();
        _sidecarHttpClient.Dispose();
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
            var existingBytes = SelectionPartialLength(work.Destination, work.Selection, work.Output);
            var reservedBytes = SelectionReservedLength(work.Destination, work.Selection, work.Output);
            var diskForecast = DiskSpacePolicy.Check(
                work.Destination,
                ForecastLength(work),
                reservedBytes,
                work.Selection.RequiresMuxing || work.Output.Kind == AudioOutputKind.Mp3);
            if (!diskForecast.IsSuccess)
            {
                await CompleteQueueRunAsync(
                    itemId,
                    DownloadQueueStatus.Failed,
                    diskForecast.Error,
                    existingBytes);
                return;
            }

            var progress = new Progress<DownloadProgress>(value => UpdateQueueProgress(itemId, value));
            TubeForgeError? downloadError;
            long completedBytes;
            using var mediaLease = await _hostRequestGate.EnterAsync(
                work.Selection.Format.Url,
                cancellation.Token);
            if (work.Output.Kind == AudioOutputKind.Mp3)
            {
                var sourcePath = AudioSourcePath(work.Destination, work.Selection.Format);
                var sourceResult = await EnsureTrackDownloadedAsync(
                    TrackRequest(work.Metadata, work.Selection.Format, sourcePath),
                    progress,
                    cancellation.Token);
                if (!sourceResult.IsSuccess)
                {
                    downloadError = sourceResult.Error;
                    completedBytes = SelectionPartialLength(work.Destination, work.Selection, work.Output);
                }
                else
                {
                    StatusMessage = $"Converting to MP3 · {work.Output.BitrateKbps} kbps";
                    var transcodeResult = await _audioTranscoder.TranscodeAsync(new AudioTranscodeRequest
                    {
                        SourcePath = sourcePath,
                        DestinationPath = work.Destination,
                        Output = work.Output,
                        AllowExistingValidatedOutput = true
                    }, cancellation.Token);
                    downloadError = transcodeResult.Error;
                    completedBytes = transcodeResult.IsSuccess
                        ? transcodeResult.Value.BytesWritten
                        : SelectionPartialLength(work.Destination, work.Selection, work.Output);
                    if (transcodeResult.IsSuccess)
                    {
                        File.Delete(sourcePath);
                    }
                }
            }
            else if (work.Selection.AudioFormat is StreamFormat audioFormat)
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
                completedBytes = result.IsSuccess
                    ? result.Value.BytesWritten
                    : SelectionPartialLength(work.Destination, work.Selection, work.Output);
            }
            else
            {
                var result = await _downloader.DownloadAsync(
                    TrackRequest(work.Metadata, work.Selection.Format, work.Destination),
                    progress,
                    cancellation.Token);
                downloadError = result.Error;
                completedBytes = result.IsSuccess
                    ? result.Value.BytesWritten
                    : SelectionPartialLength(work.Destination, work.Selection, work.Output);
            }

            if (downloadError?.Code == "Network.RateLimited")
            {
                _hostRequestGate.Defer(work.Selection.Format.Url, downloadError.RetryAfter);
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
            await RecordHistoryAsync(work, completedBytes, CancellationToken.None);
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

        var resolved = await _rateLimitedRequests.ExecuteAsync(
            YouTubeOrigin,
            token => _resolver.ResolveAsync(videoId, token),
            cancellationToken: cancellationToken);
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

        if (sourceIdentity.Output.Kind == AudioOutputKind.Mp3 && primary.Kind != StreamKind.AudioOnly)
        {
            return (null, new TubeForgeError(
                "Queue.InvalidOutputProfile",
                "The queued MP3 profile is valid only for audio-only media."));
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
        var work = new QueuedDownloadWork(
            resolved.Value.Metadata,
            selection,
            item.DestinationPath,
            sourceIdentity.Output);
        _preparedQueueWork[itemId] = work;
        return (work, null);
    }

    private static string FormatRateLimitDelay(TimeSpan delay) => delay.TotalMinutes >= 1
        ? $"{Math.Ceiling(delay.TotalMinutes)}m"
        : $"{Math.Max(1, Math.Ceiling(delay.TotalSeconds))}s";

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
        ProgressDetail = $"{_queueDispatcher.ActiveCount} active · global limit {SelectedMaxConcurrentDownloads} · host limit {MaximumConcurrentRequestsPerHost}";
    }

    private long? QueuePartialLength(Guid itemId)
    {
        if (_preparedQueueWork.TryGetValue(itemId, out var work))
        {
            return SelectionPartialLength(work.Destination, work.Selection, work.Output);
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

    private async Task RecordHistoryAsync(
        QueuedDownloadWork work,
        long bytesWritten,
        CancellationToken cancellationToken)
    {
        var entry = new DownloadHistoryEntry
        {
            Id = Guid.NewGuid(),
            VideoId = work.Metadata.Id.Value,
            SourceIdentity = SelectionIdentity(work.Metadata, work.Selection, work.Output),
            DisplayTitle = work.Metadata.Title,
            DestinationPath = work.Destination,
            BytesWritten = bytesWritten,
            CompletedAtUtc = DateTimeOffset.UtcNow
        };
        await _historyMutationLock.WaitAsync(cancellationToken);
        try
        {
            var entries = _historySnapshot.Entries
                .Where(existing =>
                    !existing.SourceIdentity.Equals(entry.SourceIdentity, StringComparison.Ordinal) &&
                    !existing.DestinationPath.Equals(entry.DestinationPath, StringComparison.OrdinalIgnoreCase))
                .Prepend(entry)
                .Take(DownloadHistoryStore.MaximumEntries)
                .ToArray();
            var next = _historySnapshot with { Entries = entries };
            var result = await _historyStore.SaveAsync(next, cancellationToken);
            if (!result.IsSuccess)
            {
                ErrorMessage = $"Download completed, but Library history could not be saved. ({result.Error!.Code})";
                return;
            }

            _historySnapshot = next;
            _historyUnavailable = false;
            RebuildHistoryItems();
        }
        finally
        {
            _historyMutationLock.Release();
        }
    }

    private async Task RemoveHistoryItemAsync(Guid itemId)
    {
        await SaveHistoryEntriesAsync(_historySnapshot.Entries.Where(entry => entry.Id != itemId).ToArray());
    }

    private async Task ClearHistoryAsync() => await SaveHistoryEntriesAsync([]);

    private async Task SaveHistoryEntriesAsync(IReadOnlyList<DownloadHistoryEntry> entries)
    {
        await _historyMutationLock.WaitAsync();
        try
        {
            var next = new DownloadHistorySnapshot { Entries = entries };
            var result = await _historyStore.SaveAsync(next);
            if (!result.IsSuccess)
            {
                ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
                return;
            }

            _historySnapshot = next;
            _historyUnavailable = false;
            RebuildHistoryItems();
            StatusMessage = entries.Count == 0 ? "Library history cleared" : "Library history updated";
        }
        finally
        {
            _historyMutationLock.Release();
        }
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

    private void RebuildHistoryItems()
    {
        HistoryItems.Clear();
        foreach (var entry in _historySnapshot.Entries.OrderByDescending(entry => entry.CompletedAtUtc))
        {
            HistoryItems.Add(new HistoryItemViewModel(entry, RevealDestination, RemoveHistoryItemAsync));
        }

        NotifyHistoryProperties();
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
        CheckForUpdatesCommand.RaiseCanExecuteChanged();
        DownloadUpdateCommand.RaiseCanExecuteChanged();
    }

    private void NotifyHistoryProperties()
    {
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HistorySummary));
        ClearHistoryCommand.RaiseCanExecuteChanged();
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
        string destination,
        AudioOutputProfile output = default)
    {
        var now = DateTimeOffset.UtcNow;
        var format = selection.Format;
        var sourceIdentity = SelectionIdentity(metadata, selection, output);
        var expectedLength = CombinedLength(selection);
        var partialLength = SelectionPartialLength(destination, selection, output);

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

    private DownloadRequest TrackRequest(
        VideoMetadata metadata,
        StreamFormat format,
        string destination) => new()
        {
            SourceUrl = format.Url,
            SourceIdentity = $"{metadata.Id.Value}:{format.FormatId}",
            DestinationPath = destination,
            ExpectedLength = format.ContentLength,
            ExpectedContainer = format.Container,
            EnableSegmentedTransfer = _settings.EnableSegmentedTransfers
        };

    private async Task<Result<DownloadReceipt>> EnsureTrackDownloadedAsync(
        DownloadRequest request,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(request.DestinationPath))
        {
            return await _downloader.DownloadAsync(request, progress, cancellationToken);
        }

        var length = new FileInfo(request.DestinationPath).Length;
        if (request.ExpectedLength is not null && request.ExpectedLength != length)
        {
            return Result<DownloadReceipt>.Failure(new TubeForgeError(
                "Media.IntermediateConflict",
                "A completed source audio track has an unexpected size."));
        }

        var validation = MediaContainerValidator.Validate(request.DestinationPath, request.ExpectedContainer);
        if (!validation.IsSuccess)
        {
            return Result<DownloadReceipt>.Failure(validation.Error!);
        }

        progress?.Report(new DownloadProgress(length, request.ExpectedLength ?? length, 0, TimeSpan.Zero));
        return Result<DownloadReceipt>.Success(new DownloadReceipt(request.DestinationPath, length, Resumed: true));
    }

    private static string IntermediateTrackPath(
        string destination,
        string role,
        StreamFormat format) =>
        destination + $".{role}-track" + FormatDisplay.OutputExtension(format);

    private static string AudioSourcePath(string destination, StreamFormat format) =>
        destination + ".source" + FormatDisplay.OutputExtension(format);

    private static string SelectionIdentity(
        VideoMetadata metadata,
        FormatItemViewModel selection,
        AudioOutputProfile output = default) =>
        DownloadSourceIdentity.Create(
            metadata.Id,
            selection.Format.FormatId,
            selection.AudioFormat?.FormatId,
            output);

    private Result<string> RenderFileName(
        VideoMetadata metadata,
        FormatItemViewModel selection,
        int? index,
        int indexWidth,
        AudioOutputProfile output = default)
    {
        var format = selection.Format;
        var quality = format.HasVideo
            ? format.Height is > 0 ? $"{format.Height}p" : "video"
            : format.Bitrate is > 0 ? $"{Math.Round(format.Bitrate.Value / 1000d):0}kbps" : "audio";
        var extension = OutputExtension(selection, output);
        var template = index is not null && FileNameTemplateText == FileNameTemplate.Default
            ? "{index} - {title}"
            : FileNameTemplateText;
        return FileNameTemplate.Render(template, new FileNameTemplateContext
        {
            Title = metadata.Title,
            Channel = metadata.Channel,
            VideoId = metadata.Id.Value,
            Quality = quality,
            Container = extension.TrimStart('.'),
            Index = index,
            IndexWidth = indexWidth
        });
    }

    private static string DuplicateDescription(DownloadDuplicateKind kind) => kind switch
    {
        DownloadDuplicateKind.QueuedOutput => "same output already queued",
        DownloadDuplicateKind.CompletedOutput => "same output already in Library",
        DownloadDuplicateKind.QueueDestination => "destination already queued",
        DownloadDuplicateKind.HistoryDestination => "destination already in Library",
        _ => "duplicate output"
    };

    private AudioOutputProfile AudioOutputFor(FormatItemViewModel selection) =>
        SelectedDownloadMode.Value == DownloadMode.AudioOnly && selection.Format.Kind == StreamKind.AudioOnly
            ? SelectedAudioProcessing.Value
            : AudioOutputProfile.Native;

    private static string OutputExtension(
        FormatItemViewModel selection,
        AudioOutputProfile output) =>
        output.Kind == AudioOutputKind.Mp3
            ? output.Extension
            : selection.RequiresMuxing
                ? FormatDisplay.Extension(selection.Format.Container)
                : FormatDisplay.OutputExtension(selection.Format);

    private static long? ForecastLength(QueuedDownloadWork work)
    {
        var sourceLength = CombinedLength(work.Selection);
        if (work.Output.Kind != AudioOutputKind.Mp3 || work.Metadata.Duration is null)
        {
            return sourceLength;
        }

        try
        {
            var mp3Length = checked((long)Math.Ceiling(
                work.Metadata.Duration.Value.TotalSeconds * work.Output.BitrateKbps * 1000d / 8d) + 64 * 1024);
            return sourceLength is null ? mp3Length : Math.Max(sourceLength.Value, mp3Length);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static long? CombinedLength(FormatItemViewModel selection)
    {
        if (selection.Format.ContentLength is null || selection.AudioFormat?.ContentLength is null)
        {
            return selection.AudioFormat is null ? selection.Format.ContentLength : null;
        }

        try
        {
            return checked(selection.Format.ContentLength.Value + selection.AudioFormat.ContentLength.Value);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static long SelectionPartialLength(
        string destination,
        FormatItemViewModel selection,
        AudioOutputProfile output = default)
    {
        if (output.Kind == AudioOutputKind.Mp3)
        {
            return CompletedOrPartialLength(AudioSourcePath(destination, selection.Format));
        }

        if (selection.AudioFormat is null)
        {
            return PartialLength(destination);
        }

        var videoPath = IntermediateTrackPath(destination, "video", selection.Format);
        var audioPath = IntermediateTrackPath(destination, "audio", selection.AudioFormat);
        try
        {
            return checked(CompletedOrPartialLength(videoPath) + CompletedOrPartialLength(audioPath));
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private static long SelectionReservedLength(
        string destination,
        FormatItemViewModel selection,
        AudioOutputProfile output = default)
    {
        if (output.Kind == AudioOutputKind.Mp3)
        {
            return CompletedOrReservedLength(AudioSourcePath(destination, selection.Format));
        }

        if (selection.AudioFormat is null)
        {
            return CompletedOrReservedLength(destination);
        }

        var videoPath = IntermediateTrackPath(destination, "video", selection.Format);
        var audioPath = IntermediateTrackPath(destination, "audio", selection.AudioFormat);
        try
        {
            return checked(CompletedOrReservedLength(videoPath) + CompletedOrReservedLength(audioPath));
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
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

        var expectedLength = item.ExpectedLength;
        if (status == DownloadQueueStatus.Completed &&
            completedBytes is > 0 &&
            DownloadSourceIdentity.TryParse(item.SourceIdentity, out var sourceIdentity) &&
            sourceIdentity.Output.Kind != AudioOutputKind.Native)
        {
            expectedLength = completedBytes;
        }

        return await UpsertQueueItemAsync(item with
        {
            ExpectedLength = expectedLength,
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
            var segmentedBytes = SegmentedTransferProgress.GetCompletedBytes(destination);
            if (segmentedBytes is not null)
            {
                return segmentedBytes.Value;
            }

            var partialPath = destination + ".part";
            return File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static long CompletedOrReservedLength(string destination)
    {
        try
        {
            var path = File.Exists(destination) ? destination : destination + ".part";
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private void ClearAnalysis()
    {
        _metadata = null;
        _collection = null;
        CollectionItems.Clear();
        CaptionTracks = [];
        SelectedCaptionTrack = null;
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
        OnPropertyChanged(nameof(HasCollection));
        OnPropertyChanged(nameof(CollectionTitle));
        OnPropertyChanged(nameof(CollectionSummary));
        OnPropertyChanged(nameof(CollectionSelectionSummary));
    }

    private void SetCollectionSelection(bool isSelected)
    {
        foreach (var item in CollectionItems)
        {
            item.IsSelected = isSelected;
        }

        CollectionSelectionChanged();
    }

    private void CollectionSelectionChanged()
    {
        OnPropertyChanged(nameof(CollectionSelectionSummary));
        SelectAllCollectionCommand.RaiseCanExecuteChanged();
        SelectNoneCollectionCommand.RaiseCanExecuteChanged();
        QueueCollectionCommand.RaiseCanExecuteChanged();
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
        OnPropertyChanged(nameof(DiagnosticsFormatSummary));
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
        OnPropertyChanged(nameof(HasCaptions));
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
        DownloadCaptionCommand.RaiseCanExecuteChanged();
        SaveThumbnailCommand.RaiseCanExecuteChanged();
        SaveMetadataCommand.RaiseCanExecuteChanged();
        SelectAllCollectionCommand.RaiseCanExecuteChanged();
        SelectNoneCollectionCommand.RaiseCanExecuteChanged();
        QueueCollectionCommand.RaiseCanExecuteChanged();
        CancelAnalysisCommand.RaiseCanExecuteChanged();
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

    private enum AppPage
    {
        Download,
        Queue,
        Library,
        Settings,
        Diagnostics
    }

    private sealed record QueuedDownloadWork(
        VideoMetadata Metadata,
        FormatItemViewModel Selection,
        string Destination,
        AudioOutputProfile Output = default);
}
