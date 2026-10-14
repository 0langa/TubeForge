using System.Buffers;

namespace TubeForge.Media;

/// <summary>
/// Drains a helper process's redirected output with bounded memory. A redirected pipe has to be
/// read, or the process blocks once the pipe buffer fills, but ReadToEndAsync keeps every
/// character until the process exits. FFmpeg normally prints a few lines, yet nothing bounds
/// what a damaged input, a broken build or a verbose log level makes it print over a long job.
/// Only the end of the diagnostic output is used: FFmpeg prints the reason it failed last.
/// </summary>
public static class ProcessOutputDrain
{
    /// <summary>Characters kept from the end of a diagnostic stream.</summary>
    public const int DefaultTailCharacters = 64 * 1024;

    private const int ChunkCharacters = 4 * 1024;

    /// <summary>
    /// Reads <paramref name="reader"/> to the end and returns at most its last
    /// <paramref name="maximumCharacters"/> characters, held in a fixed ring buffer. When earlier
    /// output was dropped, the result starts at the first whole line it kept, so it never begins
    /// with a fragment, unless no whole line fits.
    /// </summary>
    public static async Task<string> KeepTailAsync(
        TextReader reader,
        int maximumCharacters = DefaultTailCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumCharacters, 1);

        var ring = new char[maximumCharacters];
        var next = 0;
        long total = 0;
        var chunk = ArrayPool<char>.Shared.Rent(ChunkCharacters);
        try
        {
            while (true)
            {
                var count = await reader.ReadAsync(chunk.AsMemory(0, ChunkCharacters), cancellationToken)
                    .ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                total += count;
                var read = chunk.AsSpan(0, count);
                if (read.Length >= ring.Length)
                {
                    read[^ring.Length..].CopyTo(ring);
                    next = 0;
                    continue;
                }

                var untilEnd = Math.Min(read.Length, ring.Length - next);
                read[..untilEnd].CopyTo(ring.AsSpan(next));
                read[untilEnd..].CopyTo(ring);
                next = (next + read.Length) % ring.Length;
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chunk);
        }

        if (total <= ring.Length)
        {
            return new string(ring, 0, (int)total);
        }

        var tail = string.Concat(ring.AsSpan(next), ring.AsSpan(0, next));
        var lineBreak = tail.AsSpan().IndexOfAny('\r', '\n');
        if (lineBreak < 0)
        {
            return tail;
        }

        var start = lineBreak + 1;
        while (start < tail.Length && tail[start] is '\r' or '\n')
        {
            start++;
        }

        return tail.AsSpan(start).IsWhiteSpace() ? tail : tail[start..];
    }

    /// <summary>Reads <paramref name="reader"/> to the end and keeps nothing.</summary>
    public static async Task DiscardAsync(TextReader reader, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var chunk = ArrayPool<char>.Shared.Rent(ChunkCharacters);
        try
        {
            while (await reader.ReadAsync(chunk.AsMemory(0, ChunkCharacters), cancellationToken)
                       .ConfigureAwait(false) > 0)
            {
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chunk);
        }
    }
}
