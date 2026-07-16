using System.Windows;
using TubeForge.Core.Results;
using TubeForge.Installation;

namespace TubeForge.Installer;

public partial class InstallerWindow : Window
{
    private readonly Func<Task<Result<InstallationReceipt>>> _install;
    private InstallationReceipt? _receipt;

    public InstallerWindow(
        Version version,
        bool update,
        Func<Task<Result<InstallationReceipt>>> install)
    {
        _install = install ?? throw new ArgumentNullException(nameof(install));
        InitializeComponent();
        VersionText.Text = $"Version {version.ToString(3)}";
        HeadingText.Text = update ? "Update TubeForge" : "Install TubeForge";
        ActionButton.Content = update ? "Update" : "Install";
        InstallPathText.Text = InstallationPaths.CreateDefault().InstallDirectory;
    }

    private async void Action_OnClick(object sender, RoutedEventArgs e)
    {
        if (_receipt is not null)
        {
            App.Launch(_receipt.ExecutablePath);
            Close();
            return;
        }

        ActionButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = "Verifying and installing…";
        var result = await _install();
        Progress.Visibility = Visibility.Collapsed;
        if (!result.IsSuccess)
        {
            StatusText.Text = $"{result.Error!.Message} ({result.Error.Code})";
            ActionButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
            return;
        }

        _receipt = result.Value;
        StatusText.Text = result.Value.PreviousVersionRetained
            ? "Update complete. Previous version retained for rollback."
            : "Installation complete.";
        ActionButton.Content = "Launch TubeForge";
        ActionButton.IsEnabled = true;
        CancelButton.Content = "Close";
        CancelButton.IsEnabled = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => Close();
}
