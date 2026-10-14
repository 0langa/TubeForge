using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using TubeForge.App.ViewModels;
using TubeForge.Core.Errors;
using TubeForge.Tests.Framework;
using TubeForge.Updates;

namespace TubeForge.Tests.Updates;

public static class InstallerSignaturePolicyTests
{
    private const string Ours = "AA11";
    private const string Theirs = "BB22";
    private const int UntrustedRoot = unchecked((int)0x800B0109);

    [Test]
    public static void UnsignedApplicationKeepsTheHashChecksAlone()
    {
        using var files = new SignatureFiles();
        var reads = new List<string>();

        var verdict = InstallerSignaturePolicy.Check(
            files.Application,
            files.Installer,
            path =>
            {
                reads.Add(path);
                return AuthenticodeSignature.Unsigned(unchecked((int)0x800B0100));
            });

        Assert.True(verdict is null, "an unsigned build has no certificate to pin to");
        Assert.SequenceEqual(new[] { files.Application }, reads);
    }

    [Test]
    public static void SignedApplicationRequiresTheSameCertificateOnTheInstaller()
    {
        using var files = new SignatureFiles();

        Assert.True(Check(files, Signed(Ours), Signed(Ours)) is null, "same certificate, trusted");
        Assert.Equal("Update.InstallerUnsigned", Check(files, Signed(Ours), AuthenticodeSignature.Unsigned(1))?.Code);
        Assert.Equal(
            "Update.InstallerUnsigned",
            Check(files, Signed(Ours), new AuthenticodeSignature(AuthenticodeSignatureKind.Broken, unchecked((int)0x80096010), null))?.Code);
        Assert.Equal("Update.SignerMismatch", Check(files, Signed(Ours), Signed(Theirs))?.Code);
    }

    [Test]
    public static void InstallerMustBeTrustedAtLeastAsMuchAsTheRunningBuild()
    {
        using var files = new SignatureFiles();

        // A self-signed release: Windows trusts neither file's chain, but the pin holds.
        Assert.True(Check(files, Signed(Ours, UntrustedRoot), Signed(Ours, UntrustedRoot)) is null);
        // The running build is trusted, the installer is not.
        Assert.Equal(
            "Update.InstallerUntrusted",
            Check(files, Signed(Ours), Signed(Ours, UntrustedRoot))?.Code);
    }

    [Test]
    public static void ReaderRecognisesSignedTamperedAndUnsignedFiles()
    {
        using var files = new SignatureFiles();
        // The runtime's own assemblies carry an embedded Microsoft Authenticode signature.
        var signedSource = typeof(object).Assembly.Location;
        var signed = AuthenticodeReader.Read(signedSource);
        Assert.Equal(AuthenticodeSignatureKind.Signed, signed.Kind);
        Assert.True(!string.IsNullOrWhiteSpace(signed.SignerSha256));

        var tampered = Path.Combine(files.Directory, "tampered.dll");
        File.Copy(signedSource, tampered);
        using (var stream = new FileStream(tampered, FileMode.Open, FileAccess.ReadWrite))
        {
            stream.Position = stream.Length / 2;
            var value = stream.ReadByte();
            stream.Position = stream.Length / 2;
            stream.WriteByte((byte)(value ^ 0xFF));
        }

        Assert.Equal(AuthenticodeSignatureKind.Broken, AuthenticodeReader.Read(tampered).Kind);
        Assert.Equal(AuthenticodeSignatureKind.Unsigned, AuthenticodeReader.Read(files.Installer).Kind);
    }

    [Test]
    public static async Task SignedBuildRefusesAnUnsignedInstallerBeforeLaunchingIt()
    {
        var root = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", $"update-signature-{Guid.NewGuid():N}");
        var applicationData = Path.Combine(root, "appdata");
        try
        {
            var updates = Directory.CreateDirectory(Path.Combine(applicationData, "updates"));
            var installer = Path.Combine(updates.FullName, "TubeForge-2.2.2-setup.exe");
            var payload = "not a real installer"u8.ToArray();
            await File.WriteAllBytesAsync(installer, payload);
            var running = Path.Combine(root, "TubeForge.exe");
            await File.WriteAllBytesAsync(running, [0x4D, 0x5A]);

            using var viewModel = CreateViewModel(applicationData);
            SetField(viewModel, "_readyUpdate", new UpdateDownloadReceipt(
                installer,
                new Version(2, 2, 2),
                payload.LongLength,
                Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant()));
            SetField(viewModel, "_runningExecutablePath", running);
            var reads = new List<string>();
            SetField(viewModel, "_readSignature", new Func<string, AuthenticodeSignature>(path =>
            {
                reads.Add(path);
                return string.Equals(path, running, StringComparison.OrdinalIgnoreCase)
                    ? Signed(Ours)
                    : AuthenticodeSignature.Unsigned(unchecked((int)0x800B0100));
            }));

            var started = await viewModel.StartReadyUpdateAsync();

            Assert.False(started, "an unsigned installer must not run under a signed build");
            Assert.True(viewModel.HasUpdateError);
            Assert.True(
                viewModel.UpdateStatus.Contains("Update.InstallerUnsigned", StringComparison.Ordinal),
                viewModel.UpdateStatus);
            Assert.SequenceEqual(new[] { running, installer }, reads);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static TubeForgeError? Check(
        SignatureFiles files,
        AuthenticodeSignature application,
        AuthenticodeSignature installer) =>
        InstallerSignaturePolicy.Check(
            files.Application,
            files.Installer,
            path => path == files.Application ? application : installer);

    private static AuthenticodeSignature Signed(string thumbprint, int status = 0) =>
        new(AuthenticodeSignatureKind.Signed, status, thumbprint);

    private static MainViewModel CreateViewModel(string applicationData)
    {
        var constructor = typeof(MainViewModel).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(string), typeof(HttpMessageHandler), typeof(Version)],
            modifiers: null)
            ?? throw new MissingMethodException(typeof(MainViewModel).FullName, ".ctor(string, HttpMessageHandler, Version)");
        return (MainViewModel)(constructor.Invoke([applicationData, UnreachableHttp.CreateHandler(), new Version(2, 2, 1)])
            ?? throw new InvalidOperationException("The view model was not created."));
    }

    private static void SetField(MainViewModel viewModel, string name, object value)
    {
        var field = typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(typeof(MainViewModel).FullName, name);
        field.SetValue(viewModel, value);
    }

    private sealed class SignatureFiles : IDisposable
    {
        public SignatureFiles()
        {
            Directory = Path.Combine(Path.GetTempPath(), "TubeForge.Tests", $"signatures-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Directory);
            Application = Path.Combine(Directory, "TubeForge.exe");
            Installer = Path.Combine(Directory, "TubeForge-setup.exe");
            File.WriteAllBytes(Application, [0x4D, 0x5A]);
            File.WriteAllBytes(Installer, "unsigned fixture"u8.ToArray());
        }

        public string Directory { get; }

        public string Application { get; }

        public string Installer { get; }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
