namespace TubeForge.Updates;

public sealed record UpdateRelease(
    Version Version,
    Uri ReleasePage,
    string SetupAssetName,
    Uri SetupDownloadUri,
    long SetupLength,
    string SetupSha256,
    Uri ChecksumsDownloadUri,
    long ChecksumsLength,
    string ChecksumsSha256);

public sealed record UpdateDownloadReceipt(
    string InstallerPath,
    Version Version,
    long BytesWritten,
    string Sha256);
