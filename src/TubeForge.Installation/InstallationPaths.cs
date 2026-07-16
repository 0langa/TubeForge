namespace TubeForge.Installation;

public sealed record InstallationPaths(
    string ProgramsRoot,
    string InstallDirectory,
    string RollbackDirectory,
    string ApplicationDataDirectory,
    string StartMenuShortcut)
{
    public static InstallationPaths CreateDefault()
    {
        var local = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        var programs = Path.GetFullPath(Path.Combine(local, "Programs"));
        var startMenu = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));
        return new InstallationPaths(
            programs,
            Path.Combine(programs, "TubeForge"),
            Path.Combine(programs, "TubeForge.rollback"),
            Path.Combine(local, "TubeForge"),
            Path.Combine(startMenu, "Programs", "TubeForge.lnk"));
    }

    public bool IsValid()
    {
        try
        {
            var root = Path.GetFullPath(ProgramsRoot).TrimEnd(Path.DirectorySeparatorChar);
            var install = Path.GetFullPath(InstallDirectory);
            var rollback = Path.GetFullPath(RollbackDirectory);
            return Path.IsPathFullyQualified(root) &&
                   IsDirectChild(root, install) &&
                   IsDirectChild(root, rollback) &&
                   !install.Equals(rollback, StringComparison.OrdinalIgnoreCase) &&
                   Path.IsPathFullyQualified(ApplicationDataDirectory) &&
                   Path.IsPathFullyQualified(StartMenuShortcut);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsDirectChild(string parent, string child) =>
        string.Equals(
            Path.GetDirectoryName(Path.GetFullPath(child).TrimEnd(Path.DirectorySeparatorChar)),
            parent,
            StringComparison.OrdinalIgnoreCase);
}
