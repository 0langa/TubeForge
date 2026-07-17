namespace TubeForge.Installation;

public sealed record InstallationReceipt(
    string InstallDirectory,
    string ExecutablePath,
    Version Version,
    bool PreviousVersionRetained);

public sealed record UninstallReceipt(bool RemovedApplicationData);
