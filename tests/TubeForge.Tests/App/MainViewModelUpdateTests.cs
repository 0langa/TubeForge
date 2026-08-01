using System.Diagnostics;
using System.Reflection;
using TubeForge.App.ViewModels;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.App;

public static class MainViewModelUpdateTests
{
    [Test]
    public static void GeneralCommandRefreshInvalidatesUpdateAction()
    {
        using var viewModel = new MainViewModel();
        var invalidations = 0;
        viewModel.UpdateNowCommand.CanExecuteChanged += (_, _) => invalidations++;

        var refresh = typeof(MainViewModel).GetMethod(
            "RefreshCommands",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, "RefreshCommands");
        _ = refresh.Invoke(viewModel, null);

        Assert.Equal(1, invalidations);
    }

    [Test]
    public static void UpdateInstallerLaunchIsQuietWaitsForAppAndRelaunches()
    {
        var factory = typeof(MainViewModel).GetMethod(
            "CreateUpdateInstallerStartInfo",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(
                typeof(MainViewModel).FullName,
                "CreateUpdateInstallerStartInfo");

        var start = (ProcessStartInfo)(factory.Invoke(null, ["C:\\staging\\TubeForge-Setup.exe", 4321])
            ?? throw new InvalidOperationException("Update installer launch plan was not created."));

        Assert.False(start.UseShellExecute);
        Assert.Equal("C:\\staging\\TubeForge-Setup.exe", start.FileName);
        Assert.SequenceEqual(
            new[] { "/update", "/quiet", "/wait-pid", "4321", "/launch" },
            start.ArgumentList);
    }
}
