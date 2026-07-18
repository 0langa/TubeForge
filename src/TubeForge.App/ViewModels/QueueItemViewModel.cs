using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using TubeForge.App.Commands;
using TubeForge.Downloads;
using TubeForge.Downloads.Queue;

namespace TubeForge.App.ViewModels;

public sealed class QueueItemViewModel : INotifyPropertyChanged
{
    private DownloadQueueItem _item;
    private long _liveBytes;
    private string _transferDetail = string.Empty;

    public QueueItemViewModel(
        DownloadQueueItem item,
        Func<Guid, Task> resume,
        Action<Guid> pause,
        Func<Guid, Task> cancel,
        Func<Guid, Task> remove,
        Action<string> reveal)
    {
        _item = item;
        _liveBytes = item.BytesReceived;
        ResumeCommand = new AsyncRelayCommand(() => resume(Id), () => CanResume);
        PauseCommand = new RelayCommand(() => pause(Id), () => CanPause);
        CancelCommand = new AsyncRelayCommand(() => cancel(Id), () => CanCancel);
        RemoveCommand = new AsyncRelayCommand(() => remove(Id), () => CanRemove);
        RevealCommand = new RelayCommand(() => reveal(DestinationPath), () => CanReveal);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public AsyncRelayCommand ResumeCommand { get; }

    public RelayCommand PauseCommand { get; }

    public AsyncRelayCommand CancelCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    public RelayCommand RevealCommand { get; }

    public Guid Id => _item.Id;

    public string Title => _item.DisplayTitle;

    public string DestinationPath => _item.DestinationPath;

    public string FileName => Path.GetFileName(DestinationPath);

    public string DestinationDirectory => Path.GetDirectoryName(DestinationPath) ?? DestinationPath;

    public DownloadQueueStatus Status => _item.Status;

    public string StatusLabel => Status switch
    {
        DownloadQueueStatus.Queued => "QUEUED",
        DownloadQueueStatus.Downloading => "DOWNLOADING",
        DownloadQueueStatus.Paused => "PAUSED",
        DownloadQueueStatus.Completed => "COMPLETED",
        DownloadQueueStatus.Failed => "FAILED",
        DownloadQueueStatus.Cancelled => "CANCELLED",
        _ => "UNKNOWN"
    };

    public string StatusForeground => Status switch
    {
        DownloadQueueStatus.Completed => "#35E6A1",
        DownloadQueueStatus.Failed => "#FF8FA3",
        DownloadQueueStatus.Cancelled => "#FFB86B",
        DownloadQueueStatus.Paused or DownloadQueueStatus.Queued => "#F4C873",
        _ => "#AFC4FF"
    };

    public string StatusBackground => Status switch
    {
        DownloadQueueStatus.Completed => "#10251F",
        DownloadQueueStatus.Failed => "#2B1720",
        DownloadQueueStatus.Cancelled => "#2B2016",
        DownloadQueueStatus.Paused or DownloadQueueStatus.Queued => "#292414",
        _ => "#151F3A"
    };

    public string StatusBorder => Status switch
    {
        DownloadQueueStatus.Completed => "#245E4B",
        DownloadQueueStatus.Failed => "#613143",
        DownloadQueueStatus.Cancelled => "#654421",
        DownloadQueueStatus.Paused or DownloadQueueStatus.Queued => "#66592A",
        _ => "#344B82"
    };

    public double Progress => _item.ExpectedLength is > 0
        ? Math.Clamp(_liveBytes / (double)_item.ExpectedLength.Value, 0, 1)
        : Status == DownloadQueueStatus.Completed ? 1 : 0;

    public string ProgressDetail
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_transferDetail))
            {
                return _transferDetail;
            }

            var expected = _item.ExpectedLength is > 0
                ? $" of {FormatBytes(_item.ExpectedLength.Value)}"
                : string.Empty;
            var failure = string.IsNullOrWhiteSpace(_item.FailureCode)
                ? string.Empty
                : $" · {_item.FailureCode}";
            var attempt = _item.AttemptCount > 1 ? $" · attempt {_item.AttemptCount}" : string.Empty;
            return $"{FormatBytes(_liveBytes)}{expected}{attempt}{failure}";
        }
    }

    public string ResumeLabel => Status == DownloadQueueStatus.Failed ? "Retry" : "Resume";

    public bool CanResume => Status is DownloadQueueStatus.Paused or DownloadQueueStatus.Failed or DownloadQueueStatus.Cancelled;

    public bool CanPause => Status == DownloadQueueStatus.Downloading;

    public bool CanCancel => Status is DownloadQueueStatus.Queued or DownloadQueueStatus.Downloading;

    public bool CanRemove => Status != DownloadQueueStatus.Downloading;

    public bool CanReveal => Directory.Exists(DestinationDirectory);

    internal DownloadQueueItem Item => _item;

    internal void Update(DownloadQueueItem item)
    {
        _item = item;
        _liveBytes = item.BytesReceived;
        _transferDetail = string.Empty;
        NotifyAll();
    }

    internal void UpdateProgress(DownloadProgress progress)
    {
        _liveBytes = progress.BytesReceived;
        var speed = progress.BytesPerSecond > 0
            ? $" · {FormatBytes((long)progress.BytesPerSecond)}/s"
            : string.Empty;
        var eta = progress.EstimatedRemaining is not null
            ? $" · {FormatDuration(progress.EstimatedRemaining.Value)} left"
            : string.Empty;
        _transferDetail = $"{FormatBytes(progress.BytesReceived)}{speed}{eta}";
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressDetail));
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(DestinationPath));
        OnPropertyChanged(nameof(FileName));
        OnPropertyChanged(nameof(DestinationDirectory));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusForeground));
        OnPropertyChanged(nameof(StatusBackground));
        OnPropertyChanged(nameof(StatusBorder));
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressDetail));
        OnPropertyChanged(nameof(ResumeLabel));
        OnPropertyChanged(nameof(CanResume));
        OnPropertyChanged(nameof(CanPause));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(CanRemove));
        OnPropertyChanged(nameof(CanReveal));
        ResumeCommand.RaiseCanExecuteChanged();
        PauseCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
        RevealCommand.RaiseCanExecuteChanged();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");

    private static string FormatBytes(long bytes)
    {
        var megabytes = bytes / 1024d / 1024d;
        return megabytes >= 1024
            ? $"{megabytes / 1024:0.00} GB"
            : $"{megabytes:0.0} MB";
    }
}
