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

    public string FormatCountLabel => $"{Formats.Count} formats";

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
            foreach (var format in FormatRanker.RankForDownload(_metadata.Formats))
            {
                Formats.Add(new FormatItemViewModel(format));
            }

            var recommended = FormatRanker.RecommendedProgressive(_metadata.Formats) ??
                              FormatRanker.RecommendedAudio(_metadata.Formats) ??
                              _metadata.Formats.FirstOrDefault();
            SelectedFormat = Formats.FirstOrDefault(item => item.Format == recommended) ?? Formats.FirstOrDefault();
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
        ExtractionStatus = string.Empty;
        ErrorMessage = string.Empty;
        ProgressDetail = string.Empty;
        Formats.Clear();
        SelectedFormat = null;
        NotifyVideoProperties();
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
}
