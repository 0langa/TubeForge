using System.Text.Json;

namespace TubeForge.Downloads.Resume;

internal static class DownloadResumeStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    public static async Task<DownloadResumeState?> ReadAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<DownloadResumeState>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return state?.SchemaVersion == DownloadResumeState.CurrentSchemaVersion ? state : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static async Task WriteAsync(
        string path,
        DownloadResumeState state,
        CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".new";
        await using (var stream = new FileStream(
                         temporaryPath,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         bufferSize: 16 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(
                stream,
                state,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        File.Move(temporaryPath, path, overwrite: true);
    }
}
