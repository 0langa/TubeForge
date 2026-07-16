using System.ComponentModel;
using System.Runtime.CompilerServices;
using TubeForge.YouTube.Collections;

namespace TubeForge.App.ViewModels;

public sealed class CollectionItemViewModel : INotifyPropertyChanged
{
    private readonly Action _selectionChanged;
    private bool _isSelected = true;
    private string _status = string.Empty;

    public CollectionItemViewModel(YouTubeCollectionItem item, Action selectionChanged)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public YouTubeCollectionItem Item { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            _selectionChanged();
        }
    }

    public string IndexLabel => Item.Index?.ToString() ?? "—";

    public string Title => Item.Title;

    public string Detail => Item.Duration is null
        ? Item.VideoId.Value
        : $"{FormatDuration(Item.Duration.Value)} · {Item.VideoId.Value}";

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            OnPropertyChanged();
        }
    }

    public void SetStatus(string status) => Status = status;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private static string FormatDuration(TimeSpan value) => value.TotalHours >= 1
        ? value.ToString(@"h\:mm\:ss")
        : value.ToString(@"m\:ss");
}
