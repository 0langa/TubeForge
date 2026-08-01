using TubeForge.Installer;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Installation;

public static class InstallerArgumentsTests
{
    [Test]
    public static void QuietRemoveDataIsPreservedForRelocatedUninstaller()
    {
        var arguments = new InstallerArguments(["/uninstall", "/quiet", "/remove-data"]);

        Assert.True(arguments.RemoveData);
    }

    [Test]
    public static void OneClickUpdateArgumentsRequestQuietInstallWaitAndRelaunch()
    {
        var arguments = new InstallerArguments(["/update", "/quiet", "/wait-pid", "4321", "/launch"]);

        Assert.True(arguments.Has("/update"));
        Assert.True(arguments.Has("/quiet"));
        Assert.True(arguments.Has("/launch"));
        Assert.Equal(4321, arguments.WaitProcessId);
    }
}
