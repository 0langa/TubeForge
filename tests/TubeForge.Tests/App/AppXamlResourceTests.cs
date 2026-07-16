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
}
