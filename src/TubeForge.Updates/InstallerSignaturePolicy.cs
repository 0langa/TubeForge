using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using TubeForge.Core.Errors;

namespace TubeForge.Updates;

/// <summary>What Windows says about a file's embedded Authenticode signature.</summary>
/// <param name="Kind">Whether a signature is present, and whether it still matches the file.</param>
/// <param name="Status">The WinVerifyTrust result; 0 when Windows trusts the signature.</param>
/// <param name="SignerSha256">SHA-256 thumbprint of the signing certificate, when one was read.</param>
public sealed record AuthenticodeSignature(
    AuthenticodeSignatureKind Kind,
    int Status,
    string? SignerSha256)
{
    public static AuthenticodeSignature Unsigned(int status) => new(AuthenticodeSignatureKind.Unsigned, status, null);
}

public enum AuthenticodeSignatureKind
{
    /// <summary>No embedded signature (or not a signable file).</summary>
    Unsigned,

    /// <summary>A signature is present, but the file no longer matches it, or Windows distrusts its signer.</summary>
    Broken,

    /// <summary>The signature matches the file and names its signer. <see cref="AuthenticodeSignature.Status"/> says whether Windows trusts the chain.</summary>
    Signed,
}

/// <summary>
/// Decides whether a downloaded update installer may run, on top of the SHA-256
/// checks against the release API and the checksum manifest.
/// <para>
/// Those checks trust GitHub alone: the digest and <c>SHA256SUMS.txt</c> are published
/// in the same release, so a compromised token or workflow can publish an installer
/// that passes them. When the running TubeForge is Authenticode-signed, the installer
/// must also be signed by exactly the same certificate, and Windows must trust its
/// signature at least as much as the running build's. A release built without the
/// signing key then cannot update a signed installation. An unsigned TubeForge (the
/// default when the release workflow has no signing secrets) keeps the hash checks
/// alone, because there is no certificate to pin to.
/// </para>
/// </summary>
public static class InstallerSignaturePolicy
{
    /// <summary>
    /// Null when <paramref name="installerPath"/> may be launched; otherwise why not.
    /// </summary>
    public static TubeForgeError? Check(
        string runningExecutablePath,
        string installerPath,
        Func<string, AuthenticodeSignature>? readSignature = null)
    {
        readSignature ??= AuthenticodeReader.Read;
        if (string.IsNullOrWhiteSpace(runningExecutablePath) || !File.Exists(runningExecutablePath))
        {
            // No executable of our own to read a signer from (a host without a
            // process path). Nothing to pin to; the hash checks already passed.
            return null;
        }

        var application = readSignature(runningExecutablePath);
        if (application.Kind != AuthenticodeSignatureKind.Signed || application.SignerSha256 is null)
        {
            // Nothing to pin to. The hash checks already passed.
            return null;
        }

        var installer = readSignature(installerPath);
        if (installer.Kind != AuthenticodeSignatureKind.Signed || installer.SignerSha256 is null)
        {
            return new TubeForgeError(
                "Update.InstallerUnsigned",
                "This TubeForge is signed, but the downloaded installer has no valid signature.",
                $"WinVerifyTrust 0x{installer.Status:X8}");
        }

        if (!string.Equals(application.SignerSha256, installer.SignerSha256, StringComparison.OrdinalIgnoreCase))
        {
            return new TubeForgeError(
                "Update.SignerMismatch",
                "The downloaded installer is signed by a different certificate than this TubeForge.");
        }

        if (installer.Status != 0 && installer.Status != application.Status)
        {
            return new TubeForgeError(
                "Update.InstallerUntrusted",
                "Windows does not trust the downloaded installer's signature.",
                $"WinVerifyTrust 0x{installer.Status:X8}");
        }

        return null;
    }
}

/// <summary>
/// Reads embedded Authenticode signatures through WinVerifyTrust. Revocation is not
/// checked and nothing is fetched from the network: the update has to work offline
/// from its staged file, and the certificate pin is what carries the trust.
/// </summary>
public static class AuthenticodeReader
{
    private const uint WtdUiNone = 2;
    private const uint WtdRevokeNone = 0;
    private const uint WtdChoiceFile = 1;
    private const uint WtdStateActionVerify = 1;
    private const uint WtdStateActionClose = 2;
    private const uint WtdRevocationCheckNone = 0x10;
    private const uint WtdCacheOnlyUrlRetrieval = 0x1000;

    private const int TrustENoSignature = unchecked((int)0x800B0100);
    private const int TrustESubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustEProviderUnknown = unchecked((int)0x800B0001);
    private const int TrustESubjectNotTrusted = unchecked((int)0x800B0004);
    private const int TrustEBadDigest = unchecked((int)0x80096010);
    private const int TrustEExplicitDistrust = unchecked((int)0x800B0111);
    private const int CryptEFileError = unchecked((int)0x80092003);

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public static AuthenticodeSignature Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The file to verify does not exist.", fullPath);
        }

        var filePath = Marshal.StringToCoTaskMemUni(fullPath);
        var fileInfo = IntPtr.Zero;
        var action = GenericVerifyV2;
        var data = new WinTrustData();
        try
        {
            var file = new WinTrustFileInfo
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePath,
            };
            fileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(file, fileInfo, fDeleteOld: false);

            data = new WinTrustData
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = WtdUiNone,
                RevocationChecks = WtdRevokeNone,
                UnionChoice = WtdChoiceFile,
                File = fileInfo,
                StateAction = WtdStateActionVerify,
                ProvFlags = WtdRevocationCheckNone | WtdCacheOnlyUrlRetrieval,
            };

            var status = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            switch (status)
            {
                case TrustENoSignature or TrustESubjectFormUnknown or TrustEProviderUnknown or CryptEFileError:
                    return AuthenticodeSignature.Unsigned(status);
                case TrustEBadDigest or TrustEExplicitDistrust or TrustESubjectNotTrusted:
                    return new AuthenticodeSignature(AuthenticodeSignatureKind.Broken, status, null);
            }

            var signer = ReadSignerSha256(data.StateData);
            return signer is null
                ? new AuthenticodeSignature(AuthenticodeSignatureKind.Broken, status, null)
                : new AuthenticodeSignature(AuthenticodeSignatureKind.Signed, status, signer);
        }
        finally
        {
            if (data.StateData != IntPtr.Zero)
            {
                data.StateAction = WtdStateActionClose;
                _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            }

            if (fileInfo != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfo);
            }

            Marshal.FreeCoTaskMem(filePath);
        }
    }

    private static string? ReadSignerSha256(IntPtr stateData)
    {
        if (stateData == IntPtr.Zero)
        {
            return null;
        }

        var providerData = WTHelperProvDataFromStateData(stateData);
        if (providerData == IntPtr.Zero)
        {
            return null;
        }

        var signer = WTHelperGetProvSignerFromChain(providerData, 0, fCounterSigner: false, 0);
        if (signer == IntPtr.Zero)
        {
            return null;
        }

        var leaf = WTHelperGetProvCertFromChain(signer, 0);
        if (leaf == IntPtr.Zero)
        {
            return null;
        }

        var context = Marshal.PtrToStructure<CryptProviderCertHead>(leaf).Certificate;
        if (context == IntPtr.Zero)
        {
            return null;
        }

        // Duplicates the context, so the certificate outlives the trust state it came from.
        using var certificate = new X509Certificate2(context);
        return certificate.GetCertHashString(HashAlgorithmName.SHA256);
    }

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WinTrustData data);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvSignerFromChain(
        IntPtr providerData,
        uint signerIndex,
        [MarshalAs(UnmanagedType.Bool)] bool fCounterSigner,
        uint counterSignerIndex);

    [DllImport("wintrust.dll", ExactSpelling = true)]
    private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificateIndex);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint CbStruct;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint CbStruct;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    /// <summary>The leading fields of CRYPT_PROVIDER_CERT; only the certificate is read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderCertHead
    {
        public uint CbStruct;
        public IntPtr Certificate;
    }
}
