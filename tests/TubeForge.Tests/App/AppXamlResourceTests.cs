using System.Text.RegularExpressions;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.App;

public static class AppXamlResourceTests
{
    [Test]
    public static void MainWindowStaticResourcesAreDeclared()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var appXaml = File.ReadAllText(Path.Combine(fixtureDirectory, "App.xaml"));
        var mainWindowXaml = File.ReadAllText(Path.Combine(fixtureDirectory, "MainWindow.xaml"));
        var availableKeys = Regex.Matches(
                appXaml + mainWindowXaml,
                "x:Key=\"([^\"]+)\"",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var referencedKeys = Regex.Matches(
                mainWindowXaml,
                @"StaticResource\s+([^}\s]+)",
                RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);

        foreach (var key in referencedKeys)
        {
            Assert.True(availableKeys.Contains(key), $"MainWindow references undeclared StaticResource '{key}'.");
        }
    }

    [Test]
    public static void ReadOnlyProgressValuesUseOneWayBindings()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var mainWindowXaml = File.ReadAllText(Path.Combine(fixtureDirectory, "MainWindow.xaml"));
        var progressBars = Regex.Matches(
            mainWindowXaml,
            @"<ProgressBar\b.*?/>",
            RegexOptions.CultureInvariant | RegexOptions.Singleline);

        foreach (Match progressBar in progressBars)
        {
            if (progressBar.Value.Contains("Value=\"{Binding", StringComparison.Ordinal))
            {
                Assert.True(
                    progressBar.Value.Contains("Mode=OneWay", StringComparison.Ordinal),
                    $"ProgressBar value binding must be OneWay: {progressBar.Value}");
            }
        }
    }

    [Test]
    public static void CollectionFilenameGuidanceDoesNotPromiseAnUnconfiguredIndex()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var mainWindowXaml = File.ReadAllText(Path.Combine(fixtureDirectory, "MainWindow.xaml"));

        Assert.False(mainWindowXaml.Contains(
            "Filenames include collection index.",
            StringComparison.Ordinal));
        Assert.True(mainWindowXaml.Contains(
            "Use {index} in the filename template to include collection positions.",
            StringComparison.Ordinal));
    }

    [Test]
    public static void NavigationDisclosureAndRecoveryUseAccessibleControls()
    {
        var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var mainWindowXaml = File.ReadAllText(Path.Combine(fixtureDirectory, "MainWindow.xaml"));

        foreach (var icon in new[]
                 {
                     "DownloadNavIcon",
                     "QueueNavIcon",
                     "LibraryNavIcon",
                     "SettingsNavIcon",
                     "DiagnosticsNavIcon"
                 })
        {
            Assert.True(mainWindowXaml.Contains($"Data=\"{{StaticResource {icon}}}\"", StringComparison.Ordinal));
        }

        Assert.False(mainWindowXaml.Contains("Content=\"↓   Download\"", StringComparison.Ordinal));
        Assert.True(mainWindowXaml.Contains("Show advanced format controls", StringComparison.Ordinal));
        Assert.True(mainWindowXaml.Contains("Content=\"Change destination\"", StringComparison.Ordinal));
        Assert.True(mainWindowXaml.Contains("Content=\"Open diagnostics\"", StringComparison.Ordinal));
        Assert.True(mainWindowXaml.Contains("Content=\"Copy report\"", StringComparison.Ordinal));
    }
}
