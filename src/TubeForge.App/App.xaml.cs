using System.Windows;
using TubeForge.App.Diagnostics;

namespace TubeForge.App;

public partial class App : Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var probe = DesktopPerformanceProbe.TryCreate(e.Args);
        var window = new MainWindow(probe);
        MainWindow = window;
        window.Show();
    }
}
