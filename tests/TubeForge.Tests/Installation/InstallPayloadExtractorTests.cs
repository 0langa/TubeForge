using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TubeForge.Installation;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Installation;

public static class InstallPayloadExtractorTests
{
    [Test]
    public static async Task ExtractsOnlyManifestedFilesWithVerifiedDigests()
    {
        using var payload = CreatePayload();
        var staging = TemporaryPath();
        try
        {
            var result = await InstallPayloadExtractor.ExtractAsync(
                payload,
                staging,
                new Version(1, 1, 0));

            Assert.True(result.IsSuccess, result.Error?.Message);
            Assert.Equal(2, result.Value.FileCount);
            Assert.True(File.Exists(Path.Combine(staging, "TubeForge.exe")));
            Assert.True(File.Exists(Path.Combine(staging, "runtimes", "runtime.dll")));
        }
        finally
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }
        }
    }

    [Test]
    public static async Task RejectsDigestMismatchAndUnmanifestedEntry()
    {
        using var digestMismatch = CreatePayload(tamperExecutable: true);
        using var extraEntry = CreatePayload(extraPath: "extra.bin");
        var first = TemporaryPath();
        var second = TemporaryPath();
        try
        {
            var digestResult = await InstallPayloadExtractor.ExtractAsync(
                digestMismatch,
                first,
                new Version(1, 1, 0));
            var extraResult = await InstallPayloadExtractor.ExtractAsync(
                extraEntry,
                second,
                new Version(1, 1, 0));

            Assert.False(digestResult.IsSuccess);
            Assert.Equal("Install.PayloadDigestMismatch", digestResult.Error!.Code);
            Assert.False(extraResult.IsSuccess);
            Assert.Equal("Install.InvalidPayload", extraResult.Error!.Code);
        }
        finally
        {
            Delete(first);
            Delete(second);
        }
    }

    [Test]
    public static async Task RejectsTraversalAndWrongVersion()
    {
        using var traversal = CreatePayload(extraPath: "../escape.bin");
        using var valid = CreatePayload();
        var first = TemporaryPath();
        var second = TemporaryPath();
        try
        {
            var traversalResult = await InstallPayloadExtractor.ExtractAsync(
                traversal,
                first,
                new Version(1, 1, 0));
            var versionResult = await InstallPayloadExtractor.ExtractAsync(
                valid,
                second,
                new Version(1, 2, 0));

            Assert.False(traversalResult.IsSuccess);
            Assert.False(versionResult.IsSuccess);
            Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(first)!, "escape.bin")));
        }
        finally
        {
            Delete(first);
            Delete(second);
        }
    }

    private static MemoryStream CreatePayload(bool tamperExecutable = false, string? extraPath = null)
    {
        var executable = Enumerable.Range(0, 1024).Select(index => (byte)(index * 17)).ToArray();
        var runtime = Enumerable.Range(0, 512).Select(index => (byte)(index * 29)).ToArray();
        var manifest = new InstallManifest
        {
            Product = "TubeForge",
            Version = "1.1.0",
            Files =
            [
                Entry("TubeForge.exe", executable),
                Entry("runtimes/runtime.dll", runtime)
            ]
        };
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(archive, InstallPayloadExtractor.ManifestName, JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            var executablePayload = executable.ToArray();
            if (tamperExecutable)
            {
                executablePayload[^1] ^= 0xff;
            }

            Write(archive, "TubeForge.exe", executablePayload);
            Write(archive, "runtimes/runtime.dll", runtime);
            if (extraPath is not null)
            {
                Write(archive, extraPath, [1, 2, 3]);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static InstallFileEntry Entry(string path, byte[] bytes) => new()
    {
        Path = path,
        Length = bytes.LongLength,
        Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
    };

    private static void Write(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        using var output = entry.Open();
        output.Write(bytes);
    }

    private static string TemporaryPath() =>
        Path.Combine(Path.GetTempPath(), $"tubeforge-install-{Guid.NewGuid():N}");

    private static void Delete(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
