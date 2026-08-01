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
}
