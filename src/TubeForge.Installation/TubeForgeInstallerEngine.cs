using System.Diagnostics;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Installation;

public sealed class TubeForgeInstallerEngine(InstallationPaths paths)
{
    private readonly InstallationPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

    public async Task<Result<InstallationReceipt>> InstallAsync(
        Stream payload,
        Version version,
        string setupExecutablePath,
        int? waitForProcessId = null,
        bool registerShell = true,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(setupExecutablePath);
        if (!_paths.IsValid())
        {
            return Failure<InstallationReceipt>(
                "Install.InvalidLayout",
                "The per-user installation layout failed validation.");
        }

        var staging = Path.Combine(_paths.ProgramsRoot, $".TubeForge.staging-{Guid.NewGuid():N}");
        var movedExisting = false;
        var publishedNewInstall = false;
        try
        {
            await WaitForProcessAsync(waitForProcessId, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(_paths.ProgramsRoot);
            var extraction = await InstallPayloadExtractor.ExtractAsync(
                payload,
                staging,
                version,
                cancellationToken).ConfigureAwait(false);
            if (!extraction.IsSuccess)
            {
                return Result<InstallationReceipt>.Failure(extraction.Error!);
            }

            var setupSource = Path.GetFullPath(setupExecutablePath);
            if (!File.Exists(setupSource))
            {
                return Failure<InstallationReceipt>(
                    "Install.SetupMissing",
                    "The installer executable could not be retained for uninstall and repair.");
            }

            File.Copy(setupSource, Path.Combine(staging, "TubeForge.Setup.exe"), overwrite: false);
            if (Directory.Exists(_paths.RollbackDirectory))
            {
                DeleteKnownDirectory(_paths.RollbackDirectory);
            }

            if (Directory.Exists(_paths.InstallDirectory))
            {
                Directory.Move(_paths.InstallDirectory, _paths.RollbackDirectory);
                movedExisting = true;
            }

            Directory.Move(staging, _paths.InstallDirectory);
            publishedNewInstall = true;
            var executable = Path.Combine(_paths.InstallDirectory, "TubeForge.exe");
            if (registerShell)
            {
                WindowsInstallationShell.Register(_paths, version, executable);
            }

            return Result<InstallationReceipt>.Success(new InstallationReceipt(
                _paths.InstallDirectory,
                executable,
                version,
                movedExisting));
        }
        catch (OperationCanceledException)
        {
            RestoreRollbackIfNeeded(movedExisting, publishedNewInstall);
            return Failure<InstallationReceipt>("Operation.Cancelled", "The installation was cancelled.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            RestoreRollbackIfNeeded(movedExisting, publishedNewInstall);
            return Failure<InstallationReceipt>(
                "Install.Failed",
                "TubeForge could not complete the per-user installation.",
                exception.GetType().Name);
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                DeleteKnownDirectory(staging);
            }
        }
    }

    public Result<UninstallReceipt> Uninstall(bool removeApplicationData, bool unregisterShell = true)
    {
        if (!_paths.IsValid())
        {
            return Failure<UninstallReceipt>(
                "Install.InvalidLayout",
                "The per-user installation layout failed validation.");
        }

        try
        {
            DeleteKnownDirectory(_paths.InstallDirectory);
            DeleteKnownDirectory(_paths.RollbackDirectory);
            if (removeApplicationData)
            {
                DeleteExactApplicationDataDirectory();
            }

            if (unregisterShell)
            {
                WindowsInstallationShell.Unregister(_paths);
            }

            return Result<UninstallReceipt>.Success(new UninstallReceipt(removeApplicationData));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException)
        {
            return Failure<UninstallReceipt>(
                "Install.UninstallFailed",
                "TubeForge could not complete uninstall.",
                exception.GetType().Name);
        }
    }

    private static async Task WaitForProcessAsync(int? processId, CancellationToken cancellationToken)
    {
        if (processId is null || processId <= 0 || processId == Environment.ProcessId)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (ArgumentException)
        {
        }
    }

    private void RestoreRollbackIfNeeded(bool movedExisting, bool publishedNewInstall)
    {
        try
        {
            if (publishedNewInstall && Directory.Exists(_paths.InstallDirectory))
            {
                DeleteKnownDirectory(_paths.InstallDirectory);
            }

            if (movedExisting &&
                !Directory.Exists(_paths.InstallDirectory) &&
                Directory.Exists(_paths.RollbackDirectory))
            {
                Directory.Move(_paths.RollbackDirectory, _paths.InstallDirectory);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void DeleteKnownDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        var root = Path.GetFullPath(_paths.ProgramsRoot).TrimEnd(Path.DirectorySeparatorChar);
        if (!string.Equals(Path.GetDirectoryName(fullPath), root, StringComparison.OrdinalIgnoreCase) ||
            !(fullPath.Equals(_paths.InstallDirectory, StringComparison.OrdinalIgnoreCase) ||
              fullPath.Equals(_paths.RollbackDirectory, StringComparison.OrdinalIgnoreCase) ||
              Path.GetFileName(fullPath).StartsWith(".TubeForge.staging-", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Refusing to remove a directory outside the TubeForge layout.");
        }

        Directory.Delete(fullPath, recursive: true);
    }

    private void DeleteExactApplicationDataDirectory()
    {
        if (!Directory.Exists(_paths.ApplicationDataDirectory))
        {
            return;
        }

        var expected = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubeForge")).TrimEnd(Path.DirectorySeparatorChar);
        var actual = Path.GetFullPath(_paths.ApplicationDataDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to remove an unexpected application-data directory.");
        }

        Directory.Delete(actual, recursive: true);
    }

    private static Result<T> Failure<T>(string code, string message, string? detail = null) =>
        Result<T>.Failure(new TubeForgeError(code, message, detail));
}
