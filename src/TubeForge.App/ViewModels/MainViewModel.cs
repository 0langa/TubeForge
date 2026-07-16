using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Runtime.CompilerServices;
using TubeForge.App.Commands;
using TubeForge.Core.Files;
using TubeForge.Core.Media;
using TubeForge.Core.YouTube;
using TubeForge.Downloads;
using TubeForge.YouTube;

namespace TubeForge.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly IReadOnlyList<DownloadModeOption> BaseModeChoices =
    [
        new(DownloadMode.AudioVideo, "Audio + video", "Ready-to-play file with both tracks"),
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
    private CancellationTokenSource? _operationCancellation;
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
        _downloadFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        AnalyzeCommand = new AsyncRelayCommand(AnalyzeAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(UrlText));
        DownloadCommand = new AsyncRelayCommand(DownloadAsync, () => !IsBusy && SelectedFormat is not null && HasVideo);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<FormatItemViewModel> Formats { get; } = [];

    public IReadOnlyList<DownloadModeOption> DownloadModes => _downloadModes;

    public IReadOnlyList<string> AudioProcessingOptions => AudioProcessingChoices;

    public AsyncRelayCommand AnalyzeCommand { get; }

    public AsyncRelayCommand DownloadCommand { get; }

    public RelayCommand CancelCommand { get; }

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

    public string ExtractionStatus
    {
        get => _extractionStatus;
        private set => Set(ref _extractionStatus, value);
    }

    public async Task AnalyzeAsync()
    {
        CancelCurrentOperation();
        ClearAnalysis();
        var parsed = YouTubeUrlParser.ParseVideoId(UrlText);
        if (!parsed.IsSuccess)
        {
            ErrorMessage = $"{parsed.Error!.Message} ({parsed.Error.Code})";
            StatusMessage = "URL rejected";
            return;
        }

        _operationCancellation = new CancellationTokenSource();
        IsAnalyzing = true;
        StatusMessage = "Analyzing video…";
        try
        {
            var result = await _resolver.ResolveAsync(parsed.Value, _operationCancellation.Token);
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
                FormatFilter.Apply(_allFormats, new FormatSelectionCriteria { Mode = choice.Value }).Count > 0) ??
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

        CancelCurrentOperation();
        ErrorMessage = string.Empty;
        _operationCancellation = new CancellationTokenSource();
        IsDownloading = true;
        DownloadFraction = 0;
        var format = SelectedFormat.Format;
        try
        {
            var destination = FileNamePolicy.AvailablePath(
                DownloadFolder,
                _metadata.Title,
                FormatDisplay.OutputExtension(format));
            StatusMessage = "Downloading to " + Path.GetFileName(destination);
            var progress = new Progress<DownloadProgress>(UpdateProgress);
            var result = await _downloader.DownloadAsync(new DownloadRequest
            {
                SourceUrl = format.Url,
                SourceIdentity = $"{_metadata.Id.Value}:{format.FormatId}",
                DestinationPath = destination,
                ExpectedLength = format.ContentLength
            }, progress, _operationCancellation.Token);

            if (!result.IsSuccess)
            {
                ErrorMessage = $"{result.Error!.Message} ({result.Error.Code})";
                StatusMessage = result.Error.Code == "Operation.Cancelled" ? "Download cancelled" : "Download failed";
                return;
            }

            DownloadFraction = 1;
            ProgressDetail = FormatBytes(result.Value.BytesWritten) + " saved";
            StatusMessage = "Completed: " + Path.GetFileName(result.Value.DestinationPath);
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = "The download folder or generated filename is invalid. (Download.InvalidDestination)";
            ProgressDetail = exception.GetType().Name;
            StatusMessage = "Download failed";
        }
        catch (IOException exception)
        {
            ErrorMessage = "TubeForge could not prepare the destination file. (Download.WriteFailed)";
            ProgressDetail = exception.GetType().Name;
            StatusMessage = "Download failed";
        }
        finally
        {
            IsDownloading = false;
        }
    }

    public void Cancel() => _operationCancellation?.Cancel();

    public void Dispose()
    {
        CancelCurrentOperation();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private void UpdateProgress(DownloadProgress progress)
    {
        DownloadFraction = progress.Fraction ?? 0;
        var speed = progress.BytesPerSecond > 0 ? $" · {FormatBytes((long)progress.BytesPerSecond)}/s" : string.Empty;
        var eta = progress.EstimatedRemaining is not null
            ? $" · {FormatDuration(progress.EstimatedRemaining.Value)} left"
            : string.Empty;
        ProgressDetail = $"{FormatBytes(progress.BytesReceived)}{speed}{eta}";
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
            var modeFormats = FormatFilter.Apply(_allFormats, new FormatSelectionCriteria
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
                BitrateOptions = [];
                SelectedBitrate = null;
                AudioCodecOptions = [];
                SelectedAudioCodec = null;

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
                    "Any container",
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
        var combined = FormatsForMode(DownloadMode.AudioVideo);
        var audio = FormatsForMode(DownloadMode.AudioOnly);
        var video = FormatsForMode(DownloadMode.VideoOnly);

        _downloadModes =
        [
            new DownloadModeOption(
                DownloadMode.AudioVideo,
                "Audio + video",
                $"{OptionCount(combined.Count)} · up to {MaximumVideoQuality(combined)} · includes audio"),
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

    private string CombinedModeNotice()
    {
        var combined = FormatsForMode(DownloadMode.AudioVideo);
        var video = FormatsForMode(DownloadMode.VideoOnly);
        var combinedMaximum = combined.Select(format => format.Height ?? 0).DefaultIfEmpty(0).Max();
        var videoMaximum = video.Select(format => format.Height ?? 0).DefaultIfEmpty(0).Max();
        if (videoMaximum > combinedMaximum)
        {
            return $"Includes audio; combined quality stops at {MaximumVideoQuality(combined)}. " +
                   $"Choose Video only for {MaximumVideoQuality(video)}. High-quality video + audio needs TubeForge's internal muxer.";
        }

        return "Ready-to-play file with audio included. No conversion or quality loss.";
    }

    private string VideoOnlyModeNotice() =>
        $"Up to {MaximumVideoQuality(FormatsForMode(DownloadMode.VideoOnly))}. " +
        "Video track only: no audio. High-quality video + audio needs TubeForge's internal muxer.";

    private void RefreshMatchingFormats()
    {
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

        SelectedFormat = Formats.FirstOrDefault();
        OnPropertyChanged(nameof(FormatCountLabel));
        if (_metadata is not null && !IsBusy)
        {
            StatusMessage = Formats.Count > 0
                ? "Choose exact stream, then download"
                : "No stream matches these filters";
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

    private void CancelCurrentOperation()
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = null;
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
}
