using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.IO;
using System.Runtime.InteropServices;
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

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var enabled = 1;
        var handle = new WindowInteropHelper(this).Handle;
        if (DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(handle, 19, ref enabled, sizeof(int));
        }
    }

    private async void MainWindow_OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.InitializeAsync();
        if (_viewModel.ShowResponsibleUseNotice)
        {
            ResponsibleUseAcceptButton.Focus();
        }
        else
        {
            UrlTextBox.Focus();
        }
    }

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

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
