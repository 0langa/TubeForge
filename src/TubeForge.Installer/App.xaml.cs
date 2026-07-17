using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using TubeForge.Core.Results;
using TubeForge.Installation;

namespace TubeForge.Installer;

public partial class App : Application
{
    private const string PayloadResource = "TubeForge.Payload.zip";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var arguments = new InstallerArguments(e.Args);
        if (arguments.Has("/uninstall-final"))
        {
            await RunFinalUninstallAsync(arguments);
            return;
        }

        if (arguments.Has("/uninstall"))
        {
            StartRelocatedUninstaller(arguments);
            return;
        }

        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        var update = arguments.Has("/update");
        if (arguments.Has("/quiet"))
        {
            var result = await InstallAsync(version, arguments.WaitProcessId);
            if (result.IsSuccess && arguments.Has("/launch"))
            {
                Launch(result.Value.ExecutablePath);
            }

            Environment.ExitCode = result.IsSuccess ? 0 : 1;
            Shutdown(Environment.ExitCode);
            return;
        }

        var window = new InstallerWindow(
            version,
            update,
            () => InstallAsync(version, arguments.WaitProcessId));
        MainWindow = window;
        window.Closed += (_, _) => Shutdown(Environment.ExitCode);
        window.Show();
    }

    private static async Task<Result<InstallationReceipt>> InstallAsync(Version version, int? waitProcessId)
    {
        var payload = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource);
        if (payload is null)
        {
            return Result<InstallationReceipt>.Failure(new TubeForge.Core.Errors.TubeForgeError(
                "Install.PayloadMissing",
                "This setup executable does not contain a TubeForge application payload."));
        }

        await using (payload)
        {
            return await new TubeForgeInstallerEngine(InstallationPaths.CreateDefault()).InstallAsync(
                payload,
                version,
                Environment.ProcessPath ?? throw new InvalidOperationException("Setup process path is unavailable."),
                waitProcessId);
        }
    }

    private async Task RunFinalUninstallAsync(InstallerArguments arguments)
    {
        await WaitForProcessAsync(arguments.WaitProcessId);
        var result = new TubeForgeInstallerEngine(InstallationPaths.CreateDefault()).Uninstall(
            arguments.Has("/remove-data"));
        if (!arguments.Has("/quiet"))
        {
            MessageBox.Show(
                result.IsSuccess
                    ? "TubeForge was removed. Downloaded media was left unchanged."
                    : $"Uninstall failed: {result.Error!.Message} ({result.Error.Code})",
                "TubeForge Setup",
                MessageBoxButton.OK,
                result.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Error);
        }

        if (Environment.ProcessPath is string current)
        {
            _ = MoveFileEx(current, null, 4);
        }

        Environment.ExitCode = result.IsSuccess ? 0 : 1;
        Shutdown(Environment.ExitCode);
    }

    private void StartRelocatedUninstaller(InstallerArguments arguments)
    {
        var removeData = false;
        if (!arguments.Has("/quiet"))
        {
            var choice = MessageBox.Show(
                "Remove local settings, queue, and Library history too?\n\nDownloaded media in your chosen folders is never removed.",
                "Uninstall TubeForge",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            if (choice == MessageBoxResult.Cancel)
            {
                Shutdown();
                return;
            }

            removeData = choice == MessageBoxResult.Yes;
        }

        var current = Environment.ProcessPath
            ?? throw new InvalidOperationException("Setup process path is unavailable.");
        var relocated = Path.Combine(Path.GetTempPath(), $"TubeForge-Uninstall-{Guid.NewGuid():N}.exe");
        File.Copy(current, relocated, overwrite: false);
        var start = new ProcessStartInfo
        {
            FileName = relocated,
            UseShellExecute = false
        };
        start.ArgumentList.Add("/uninstall-final");
        start.ArgumentList.Add("/wait-pid");
        start.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (removeData)
        {
            start.ArgumentList.Add("/remove-data");
        }

        if (arguments.Has("/quiet"))
        {
            start.ArgumentList.Add("/quiet");
        }

        _ = Process.Start(start) ?? throw new InvalidOperationException("Could not start relocated uninstaller.");
        Shutdown();
    }

    private static async Task WaitForProcessAsync(int? processId)
    {
        if (processId is null || processId <= 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (ArgumentException)
        {
        }
        catch (OperationCanceledException)
        {
        }
    }

    internal static void Launch(string executable) => _ = Process.Start(new ProcessStartInfo
    {
        FileName = executable,
        UseShellExecute = true
    });

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFile, string? newFile, uint flags);
}

internal sealed class InstallerArguments(string[] values)
{
    public bool Has(string value) => values.Contains(value, StringComparer.OrdinalIgnoreCase);

    public int? WaitProcessId
    {
        get
        {
            for (var index = 0; index + 1 < values.Length; index++)
            {
                if (values[index].Equals("/wait-pid", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(values[index + 1], out var processId) && processId > 0)
                {
                    return processId;
                }
            }

            return null;
        }
    }
}
