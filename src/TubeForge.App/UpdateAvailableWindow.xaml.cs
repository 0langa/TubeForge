using System.ComponentModel;
using System.Windows;
using TubeForge.App.ViewModels;

namespace TubeForge.App;

public partial class UpdateAvailableWindow : Window
{
    private readonly MainViewModel _viewModel;

    public UpdateAvailableWindow(MainViewModel viewModel, Version version)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(version);
        InitializeComponent();
        DataContext = viewModel;
        VersionText.Text = $"TubeForge {version.ToString(3)}";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_viewModel.CanDismissUpdatePrompt)
        {
            e.Cancel = true;
        }

        base.OnClosing(e);
    }

    private void LaterButton_OnClick(object sender, RoutedEventArgs e) => Close();
}
