using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TubeForge.Installation;

public static class WindowsInstallationShell
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\TubeForge";

    public static void Register(InstallationPaths paths, Version version, string executablePath)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var executable = Path.GetFullPath(executablePath);
        var setup = Path.Combine(paths.InstallDirectory, "TubeForge.Setup.exe");
        if (!File.Exists(executable) || !File.Exists(setup))
        {
            throw new IOException("Installed application files are incomplete.");
        }

        CreateShortcut(paths.StartMenuShortcut, executable, paths.InstallDirectory);
        using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true)
            ?? throw new IOException("Could not create the per-user uninstall registry entry.");
        key.SetValue("DisplayName", "TubeForge", RegistryValueKind.String);
        key.SetValue("DisplayVersion", version.ToString(3), RegistryValueKind.String);
        key.SetValue("Publisher", "0langa", RegistryValueKind.String);
        key.SetValue("InstallLocation", paths.InstallDirectory, RegistryValueKind.String);
        key.SetValue("DisplayIcon", $"\"{executable}\",0", RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{setup}\" /uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{setup}\" /uninstall /quiet", RegistryValueKind.String);
        key.SetValue("EstimatedSize", EstimatedKilobytes(paths.InstallDirectory), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    public static void Unregister(InstallationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        try
        {
            File.Delete(paths.StartMenuShortcut);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
    }

    private static void CreateShortcut(string shortcutPath, string executable, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellLinkType = Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-c000-000000000046"), throwOnError: true)!;
        var shellLink = (IShellLinkW)(Activator.CreateInstance(shellLinkType)
            ?? throw new IOException("Could not create the Windows shortcut service."));
        try
        {
            Check(shellLink.SetPath(executable));
            Check(shellLink.SetWorkingDirectory(workingDirectory));
            Check(shellLink.SetDescription("TubeForge media downloader"));
            Check(shellLink.SetIconLocation(executable, 0));
            var persist = (IPersistFile)shellLink;
            Check(persist.Save(shortcutPath, remember: true));
        }
        finally
        {
            if (Marshal.IsComObject(shellLink))
            {
                _ = Marshal.FinalReleaseComObject(shellLink);
            }
        }
    }

    private static int EstimatedKilobytes(string directory)
    {
        long total = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            try
            {
                total = checked(total + new FileInfo(file).Length);
            }
            catch (OverflowException)
            {
                return int.MaxValue;
            }
        }

        return checked((int)Math.Min(int.MaxValue, Math.Max(1, (total + 1023) / 1024)));
    }

    private static void Check(int result)
    {
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }
    }
}

[ComImport]
[Guid("000214f9-0000-0000-c000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    [PreserveSig] int GetPath(IntPtr file, int maximumPath, IntPtr findData, uint flags);
    [PreserveSig] int GetIdList(IntPtr itemIdList);
    [PreserveSig] int SetIdList(IntPtr itemIdList);
    [PreserveSig] int GetDescription(IntPtr description, int maximumName);
    [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
    [PreserveSig] int GetWorkingDirectory(IntPtr directory, int maximumPath);
    [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
    [PreserveSig] int GetArguments(IntPtr arguments, int maximumPath);
    [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
    [PreserveSig] int GetHotkey(out ushort hotkey);
    [PreserveSig] int SetHotkey(ushort hotkey);
    [PreserveSig] int GetShowCommand(out int showCommand);
    [PreserveSig] int SetShowCommand(int showCommand);
    [PreserveSig] int GetIconLocation(IntPtr iconPath, int iconPathLength, out int iconIndex);
    [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
    [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
    [PreserveSig] int Resolve(IntPtr windowHandle, uint flags);
    [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
}

[ComImport]
[Guid("0000010b-0000-0000-c000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    [PreserveSig] int GetClassId(out Guid classId);
    [PreserveSig] int IsDirty();
    [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
    [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
    [PreserveSig] int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
    [PreserveSig] int GetCurrentFile(IntPtr fileName);
}
