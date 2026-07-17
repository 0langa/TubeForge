using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TubeForge.Installation;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Installation;

public static class TubeForgeInstallerEngineTests
{
    [Test]
    public static async Task InstallsUpdatesRetainsRollbackAndUninstallsProgramFiles()
    {
        using var root = new TestRoot();
        var paths = root.Paths();
        var engine = new TubeForgeInstallerEngine(paths);
        var setup = root.File("downloaded-setup.exe", [7, 8, 9]);
        using var firstPayload = Payload(new Version(1, 1, 0), [1, 2, 3]);
        using var secondPayload = Payload(new Version(1, 2, 0), [4, 5, 6]);

        var first = await engine.InstallAsync(
            firstPayload,
            new Version(1, 1, 0),
            setup,
            registerShell: false);
        Assert.True(first.IsSuccess, first.Error?.Message);
        Assert.SequenceEqual(new byte[] { 1, 2, 3 }, File.ReadAllBytes(first.Value.ExecutablePath));
        Assert.True(File.Exists(Path.Combine(paths.InstallDirectory, "TubeForge.Setup.exe")));

        var second = await engine.InstallAsync(
            secondPayload,
            new Version(1, 2, 0),
            setup,
            registerShell: false);
        Assert.True(second.IsSuccess, second.Error?.Message);
        Assert.True(second.Value.PreviousVersionRetained);
        Assert.SequenceEqual(new byte[] { 4, 5, 6 }, File.ReadAllBytes(second.Value.ExecutablePath));
        Assert.SequenceEqual(
            new byte[] { 1, 2, 3 },
            File.ReadAllBytes(Path.Combine(paths.RollbackDirectory, "TubeForge.exe")));

        Directory.CreateDirectory(paths.ApplicationDataDirectory);
        File.WriteAllText(Path.Combine(paths.ApplicationDataDirectory, "settings.json"), "{}");
        var uninstall = engine.Uninstall(removeApplicationData: false, unregisterShell: false);
        Assert.True(uninstall.IsSuccess, uninstall.Error?.Message);
        Assert.False(Directory.Exists(paths.InstallDirectory));
        Assert.False(Directory.Exists(paths.RollbackDirectory));
        Assert.True(File.Exists(Path.Combine(paths.ApplicationDataDirectory, "settings.json")));
    }

    [Test]
    public static async Task InvalidUpgradePayloadLeavesExistingInstallUntouched()
    {
        using var root = new TestRoot();
        var paths = root.Paths();
        var engine = new TubeForgeInstallerEngine(paths);
        var setup = root.File("setup.exe", [7, 8, 9]);
        using var valid = Payload(new Version(1, 1, 0), [1, 2, 3]);
        var installed = await engine.InstallAsync(
            valid,
            new Version(1, 1, 0),
            setup,
            registerShell: false);
        Assert.True(installed.IsSuccess);

        using var invalid = Payload(new Version(1, 2, 0), [4, 5, 6], tamper: true);
        var upgrade = await engine.InstallAsync(
            invalid,
            new Version(1, 2, 0),
            setup,
            registerShell: false);
        Assert.False(upgrade.IsSuccess);
        Assert.SequenceEqual(
            new byte[] { 1, 2, 3 },
            File.ReadAllBytes(Path.Combine(paths.InstallDirectory, "TubeForge.exe")));
    }

    private static MemoryStream Payload(Version version, byte[] executable, bool tamper = false)
    {
        var manifest = new InstallManifest
        {
            Product = "TubeForge",
            Version = version.ToString(3),
            Files =
            [
                new InstallFileEntry
                {
                    Path = "TubeForge.exe",
                    Length = executable.LongLength,
                    Sha256 = Convert.ToHexString(SHA256.HashData(executable))
                }
            ]
        };
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, InstallPayloadExtractor.ManifestName, JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var bytes = executable.ToArray();
            if (tamper)
            {
                bytes[^1] ^= 0xff;
            }

            Write(archive, "TubeForge.exe", bytes);
        }

        stream.Position = 0;
        return stream;
    }

    private static void Write(ZipArchive archive, string name, byte[] bytes)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private sealed class TestRoot : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"tubeforge-installer-{Guid.NewGuid():N}");

        public TestRoot() => Directory.CreateDirectory(_root);

        public InstallationPaths Paths()
        {
            var programs = Path.Combine(_root, "Programs");
            return new InstallationPaths(
                programs,
                Path.Combine(programs, "TubeForge"),
                Path.Combine(programs, "TubeForge.rollback"),
                Path.Combine(_root, "Data", "TubeForge"),
                Path.Combine(_root, "Start Menu", "Programs", "TubeForge.lnk"));
        }

        public string File(string name, byte[] contents)
        {
            var path = Path.Combine(_root, name);
            System.IO.File.WriteAllBytes(path, contents);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
