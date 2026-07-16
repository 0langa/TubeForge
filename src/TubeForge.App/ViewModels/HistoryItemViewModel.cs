using System.IO;
using TubeForge.App.Commands;
using TubeForge.Downloads.History;

namespace TubeForge.App.ViewModels;

public sealed class HistoryItemViewModel
{
    public HistoryItemViewModel(
        DownloadHistoryEntry entry,
        Action<string> reveal,
        Func<Guid, Task> remove)
    {
        Entry = entry ?? throw new ArgumentNullException(nameof(entry));
        ArgumentNullException.ThrowIfNull(reveal);
        ArgumentNullException.ThrowIfNull(remove);
        RevealCommand = new RelayCommand(
            () => reveal(Entry.DestinationPath),
            () => File.Exists(Entry.DestinationPath));
        RemoveCommand = new AsyncRelayCommand(() => remove(Entry.Id));
    }

    public DownloadHistoryEntry Entry { get; }

    public Guid Id => Entry.Id;

    public string Title => Entry.DisplayTitle;

    public string Detail =>
        $"{Entry.CompletedAtUtc.ToLocalTime():g} · {FormatBytes(Entry.BytesWritten)} · " +
        (File.Exists(Entry.DestinationPath) ? "file available" : "file moved or deleted");

    public string Destination => Entry.DestinationPath;

    public RelayCommand RevealCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    private static string FormatBytes(long bytes)
    {
        var megabytes = bytes / 1024d / 1024d;
        return megabytes >= 1024
            ? $"{megabytes / 1024:0.00} GB"
            : $"{megabytes:0.#} MB";
    }
}
