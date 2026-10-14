using System.Globalization;
using System.Text;
using TubeForge.Media;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Media;

public static class ProcessOutputDrainTests
{
    private const string FinalLines =
        "Error while decoding stream #0:1: Invalid data found when processing input\r\n" +
        "Conversion failed!\r\n";

    [Test]
    public static async Task FfmpegRunnerKeepsOnlyTheTailOfChattyDiagnostics()
    {
        using var directory = new MediaTestDirectory();
        var log = new StringBuilder();
        for (var line = 0; line < 50_000; line++)
        {
            log.Append("[fixture] repeated decode error ")
                .Append(line.ToString("D6", CultureInfo.InvariantCulture))
                .Append("\r\n");
        }

        log.Append(FinalLines);
        var fullLog = log.ToString();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "chatty.log"), fullLog, Encoding.ASCII);

        // A stand-in for a chatty FFmpeg: cmd.exe writes the 2 MB log to stdout, then to stderr,
        // and fails. Nothing is downloaded and no FFmpeg is needed.
        var run = await new FfmpegProcessRunner().RunWithDiagnosticsAsync(
                Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                ["/d", "/c", "type", "chatty.log", "&", "type", "chatty.log", "1>&2", "&", "exit", "/b", "1"],
                directory.Path,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, run.ExitCode);
        Assert.True(
            run.StandardError.Length <= 64 * 1024,
            $"Kept {run.StandardError.Length} characters of a {fullLog.Length}-character log.");
        Assert.True(
            run.StandardError.StartsWith("[fixture] repeated decode error ", StringComparison.Ordinal),
            "The kept diagnostics start in the middle of a line.");
        Assert.True(run.StandardError.EndsWith(FinalLines, StringComparison.Ordinal));
        // The user-facing failure reason comes from the last lines, so the tail gives the same one.
        Assert.Equal(FfmpegDiagnostics.Summarize(fullLog), FfmpegDiagnostics.Summarize(run.StandardError));
        Assert.Equal(
            "Error while decoding stream #0:1: Invalid data found when processing input | Conversion failed!",
            FfmpegDiagnostics.Summarize(run.StandardError));
    }

    [Test]
    public static async Task KeepTailReturnsOutputWithinTheLimitUnchanged()
    {
        Assert.Equal(string.Empty, await ProcessOutputDrain.KeepTailAsync(new StringReader(string.Empty)));
        Assert.Equal(
            "first\r\nsecond\r\n",
            await ProcessOutputDrain.KeepTailAsync(new StringReader("first\r\nsecond\r\n"), maximumCharacters: 15));
        Assert.Equal(
            "partial line\nlast\n",
            await ProcessOutputDrain.KeepTailAsync(new StringReader("partial line\nlast\n"), maximumCharacters: 18));
    }

    [Test]
    public static async Task KeepTailWrapsTheRingAndStartsAtAWholeLine()
    {
        // 4,096-character reads wrap a 10,000-character ring at a different offset each time.
        var text = string.Concat(Enumerable.Range(0, 5_000)
            .Select(line => "line " + line.ToString("D5", CultureInfo.InvariantCulture) + "\n"));
        var lastWindow = text[^10_000..];
        var expected = lastWindow[(lastWindow.IndexOf('\n', StringComparison.Ordinal) + 1)..];

        var tail = await ProcessOutputDrain.KeepTailAsync(new StringReader(text), maximumCharacters: 10_000);

        Assert.Equal(expected, tail);
        Assert.True(tail.StartsWith("line ", StringComparison.Ordinal));
        Assert.True(tail.EndsWith("line 04999\n", StringComparison.Ordinal));
    }

    [Test]
    public static async Task KeepTailKeepsAFragmentWhenNoWholeLineFits()
    {
        var unbroken = new string('x', 20_000);

        var tail = await ProcessOutputDrain.KeepTailAsync(new StringReader(unbroken), maximumCharacters: 100);
        var terminated = await ProcessOutputDrain.KeepTailAsync(
            new StringReader(unbroken + "\r\n"),
            maximumCharacters: 100);
        var crlfAtCut = await ProcessOutputDrain.KeepTailAsync(
            new StringReader("dropped\r\nkept line\r\n"),
            maximumCharacters: 12);

        Assert.Equal(new string('x', 100), tail);
        Assert.Equal(new string('x', 98) + "\r\n", terminated);
        Assert.Equal("kept line\r\n", crlfAtCut);
    }

    [Test]
    public static async Task DiscardReadsToTheEndWithoutKeepingOutput()
    {
        using var reader = new StringReader(new string('x', 100_000));

        await ProcessOutputDrain.DiscardAsync(reader);

        Assert.Equal(-1, reader.Peek());
    }
}
