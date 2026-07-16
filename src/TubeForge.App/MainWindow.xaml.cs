using System.Windows;
using System.Windows.Input;
using System.IO;
using Microsoft.Win32;
using TubeForge.App.ViewModels;

namespace TubeForge.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        Loaded += MainWindow_OnLoaded;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e) =>
        await _viewModel.InitializeAsync();

    private void BrowseButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose download folder",
            InitialDirectory = Directory.Exists(_viewModel.DownloadFolder)
                ? _viewModel.DownloadFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.DownloadFolder = dialog.FolderName;
        }
    }

    private void UrlTextBox_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.AnalyzeCommand.CanExecute(null))
        {
            _viewModel.AnalyzeCommand.Execute(null);
            e.Handled = true;
        }
    }
}
