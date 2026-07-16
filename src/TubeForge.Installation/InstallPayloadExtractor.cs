using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Installation;

public static class InstallPayloadExtractor
{
    public const string ManifestName = "install-manifest.json";
    private const int MaximumFiles = 4096;
    private const long MaximumManifestBytes = 1024 * 1024;
    private const long MaximumFileBytes = 512L * 1024 * 1024;
    private const long MaximumPayloadBytes = 1024L * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 16,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task<Result<InstallPayloadReceipt>> ExtractAsync(
        Stream payload,
        string stagingDirectory,
        Version expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        var staging = Path.GetFullPath(stagingDirectory);
        try
        {
            if (Directory.Exists(staging) || File.Exists(staging))
            {
                return Failure("Install.StagingExists", "The installer staging path already exists.");
            }

            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count is 0 or > MaximumFiles + 1)
            {
                return InvalidPayload();
            }

            var manifestEntries = archive.Entries
                .Where(entry => entry.FullName.Equals(ManifestName, StringComparison.Ordinal))
                .ToArray();
            if (manifestEntries.Length != 1 ||
                manifestEntries[0].Length is <= 0 or > MaximumManifestBytes)
            {
                return InvalidPayload();
            }

            InstallManifest? manifest;
            await using (var manifestStream = manifestEntries[0].Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<InstallManifest>(
                    manifestStream,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);
            }

            var manifestValidation = ValidateManifest(manifest, expectedVersion);
            if (manifestValidation is not null)
            {
                return Result<InstallPayloadReceipt>.Failure(manifestValidation);
            }

            var files = manifest!.Files.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
            var payloadEntries = archive.Entries
                .Where(entry => !entry.FullName.Equals(ManifestName, StringComparison.Ordinal))
                .ToArray();
            if (payloadEntries.Length != files.Count ||
                payloadEntries.Any(entry => !files.ContainsKey(entry.FullName)))
            {
                return InvalidPayload();
            }

            Directory.CreateDirectory(staging);
            long total = 0;
            foreach (var entry in payloadEntries.OrderBy(entry => entry.FullName, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var expected = files[entry.FullName];
                if (entry.Length != expected.Length || entry.Length > MaximumFileBytes)
                {
                    return InvalidPayload();
                }

                total = checked(total + entry.Length);
                if (total > MaximumPayloadBytes)
                {
                    return InvalidPayload();
                }

                var destination = ResolvePayloadPath(staging, entry.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var input = entry.Open();
                await using var output = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    128 * 1024,
                    FileOptions.Asynchronous | FileOptions.WriteThrough);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[128 * 1024];
                long written = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    written = checked(written + read);
                    if (written > expected.Length)
                    {
                        return InvalidPayload();
                    }

                    hash.AppendData(buffer.AsSpan(0, read));
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                var actualHash = Convert.ToHexString(hash.GetHashAndReset());
                if (written != expected.Length ||
                    !actualHash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Failure(
                        "Install.PayloadDigestMismatch",
                        "An installer payload file failed SHA-256 validation.");
                }
            }

            if (!File.Exists(Path.Combine(staging, "TubeForge.exe")))
            {
                return InvalidPayload();
            }

            return Result<InstallPayloadReceipt>.Success(new InstallPayloadReceipt(
                staging,
                expectedVersion,
                files.Count,
                total));
        }
        catch (OperationCanceledException)
        {
            TryDeleteDirectory(staging);
            return Failure("Operation.Cancelled", "The installation was cancelled.");
        }
        catch (Exception exception) when (exception is InvalidDataException or JsonException or IOException or
                                          UnauthorizedAccessException or OverflowException)
        {
            TryDeleteDirectory(staging);
            return Failure(
                "Install.InvalidPayload",
                "The installer payload was malformed or could not be staged.",
                exception.GetType().Name);
        }
    }

    private static TubeForgeError? ValidateManifest(InstallManifest? manifest, Version expectedVersion)
    {
        if (manifest is null ||
            manifest.SchemaVersion != InstallManifest.CurrentSchemaVersion ||
            manifest.Product != "TubeForge" ||
            !Version.TryParse(manifest.Version, out var version) ||
            version != expectedVersion ||
            manifest.Files.Count is 0 or > MaximumFiles)
        {
            return InvalidPayload().Error;
        }

        var paths = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (!IsSafePayloadPath(file.Path) ||
                !paths.Add(file.Path) ||
                file.Length < 0 || file.Length > MaximumFileBytes ||
                file.Sha256.Length != 64 || file.Sha256.Any(character => !char.IsAsciiHexDigit(character)))
            {
                return InvalidPayload().Error;
            }

            try
            {
                total = checked(total + file.Length);
            }
            catch (OverflowException)
            {
                return InvalidPayload().Error;
            }
        }

        return total <= MaximumPayloadBytes ? null : InvalidPayload().Error;
    }

    private static bool IsSafePayloadPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path.Contains('\\') ||
            path.StartsWith('/') || path.EndsWith('/') || path.Contains(':'))
        {
            return false;
        }

        return path.Split('/').All(segment =>
            segment.Length > 0 && segment is not "." and not ".." &&
            segment.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    private static string ResolvePayloadPath(string staging, string relativePath)
    {
        if (!IsSafePayloadPath(relativePath))
        {
            throw new InvalidDataException("Payload path failed validation.");
        }

        var destination = Path.GetFullPath(Path.Combine(
            staging,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Payload path escaped staging directory.");
        }

        return destination;
    }

    private static Result<InstallPayloadReceipt> InvalidPayload() => Failure(
        "Install.InvalidPayload",
        "The installer payload failed structure validation.");

    private static Result<InstallPayloadReceipt> Failure(
        string code,
        string message,
        string? detail = null) =>
        Result<InstallPayloadReceipt>.Failure(new TubeForgeError(code, message, detail));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
