using System.Globalization;
using System.Net;
using System.Text;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.YouTube.Captions;

public static class WebVttCaptionConverter
{
    private const int MaximumCues = 100_000;

    public static Result<string> Normalize(string source)
    {
        var parsed = Parse(source);
        if (!parsed.IsSuccess)
        {
            return Result<string>.Failure(parsed.Error!);
        }

        var output = new StringBuilder("WEBVTT\n\n");
        foreach (var cue in parsed.Value)
        {
            output.Append(FormatTimestamp(cue.Start, '.'))
                .Append(" --> ")
                .Append(FormatTimestamp(cue.End, '.'))
                .Append('\n')
                .Append(cue.Text)
                .Append("\n\n");
        }

        return Result<string>.Success(output.ToString());
    }

    public static Result<string> ConvertToSubRip(string source)
    {
        var parsed = Parse(source);
        if (!parsed.IsSuccess)
        {
            return Result<string>.Failure(parsed.Error!);
        }

        var output = new StringBuilder();
        for (var index = 0; index < parsed.Value.Count; index++)
        {
            var cue = parsed.Value[index];
            output.Append(index + 1)
                .Append('\n')
                .Append(FormatTimestamp(cue.Start, ','))
                .Append(" --> ")
                .Append(FormatTimestamp(cue.End, ','))
                .Append('\n')
                .Append(SanitizeSubRipText(cue.Text))
                .Append("\n\n");
        }

        return Result<string>.Success(output.ToString());
    }

    public static Result<int> CountCues(string source)
    {
        var parsed = Parse(source);
        return parsed.IsSuccess
            ? Result<int>.Success(parsed.Value.Count)
            : Result<int>.Failure(parsed.Error!);
    }

    private static Result<IReadOnlyList<CaptionCue>> Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source) || source.Contains('\0'))
        {
            return Failure<IReadOnlyList<CaptionCue>>("The WebVTT caption document is empty or invalid.");
        }

        var normalized = source.TrimStart('\uFEFF').Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = normalized.Split('\n');
        if (lines.Length == 0 ||
            !(lines[0].Equals("WEBVTT", StringComparison.Ordinal) ||
              lines[0].StartsWith("WEBVTT ", StringComparison.Ordinal) ||
              lines[0].StartsWith("WEBVTT\t", StringComparison.Ordinal)))
        {
            return Failure<IReadOnlyList<CaptionCue>>("The caption response is not a WebVTT document.");
        }

        var index = 1;
        while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
        {
            index++;
        }

        var cues = new List<CaptionCue>();
        while (index < lines.Length)
        {
            while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
            {
                index++;
            }

            if (index >= lines.Length)
            {
                break;
            }

            if (IsMetadataBlock(lines[index]))
            {
                while (index < lines.Length && !string.IsNullOrWhiteSpace(lines[index]))
                {
                    index++;
                }

                continue;
            }

            var timingLine = lines[index];
            if (!timingLine.Contains("-->", StringComparison.Ordinal))
            {
                index++;
                if (index >= lines.Length)
                {
                    return Failure<IReadOnlyList<CaptionCue>>("A WebVTT cue is missing its timing line.");
                }

                timingLine = lines[index];
            }

            if (!TryParseTiming(timingLine, out var start, out var end) || end < start)
            {
                return Failure<IReadOnlyList<CaptionCue>>("A WebVTT cue contains an invalid time range.");
            }

            index++;
            var cueLines = new List<string>();
            while (index < lines.Length && lines[index].Length > 0)
            {
                if (lines[index].Any(character => char.IsControl(character) && character != '\t'))
                {
                    return Failure<IReadOnlyList<CaptionCue>>("A WebVTT cue contains unsafe control characters.");
                }

                cueLines.Add(lines[index].TrimEnd());
                index++;
            }

            if (cueLines.Count == 0)
            {
                continue;
            }

            cues.Add(new CaptionCue(start, end, string.Join('\n', cueLines)));
            if (cues.Count > MaximumCues)
            {
                return Failure<IReadOnlyList<CaptionCue>>("The WebVTT caption document contains too many cues.");
            }
        }

        return Result<IReadOnlyList<CaptionCue>>.Success(cues);
    }

    private static bool TryParseTiming(string line, out TimeSpan start, out TimeSpan end)
    {
        start = default;
        end = default;
        var arrow = line.IndexOf("-->", StringComparison.Ordinal);
        if (arrow < 0 || !TryParseTimestamp(line[..arrow].Trim(), out start))
        {
            return false;
        }

        var endAndSettings = line[(arrow + 3)..].TrimStart();
        var separator = endAndSettings.IndexOfAny([' ', '\t']);
        var endText = separator < 0 ? endAndSettings : endAndSettings[..separator];
        return TryParseTimestamp(endText, out end);
    }

    private static bool TryParseTimestamp(string value, out TimeSpan timestamp)
    {
        timestamp = default;
        var parts = value.Split(':');
        if (parts.Length is not (2 or 3))
        {
            return false;
        }

        var secondParts = parts[^1].Split('.');
        if (secondParts.Length != 2 || secondParts[1].Length != 3 ||
            !int.TryParse(secondParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) ||
            !int.TryParse(secondParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds) ||
            !int.TryParse(parts[^2], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) ||
            seconds is < 0 or >= 60 || minutes is < 0 or >= 60 || milliseconds is < 0 or >= 1_000)
        {
            return false;
        }

        var hours = 0;
        if (parts.Length == 3 &&
            (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out hours) ||
             hours is < 0 or > 9999))
        {
            return false;
        }

        try
        {
            timestamp = TimeSpan.FromMilliseconds(checked(
                (((long)hours * 60 + minutes) * 60 + seconds) * 1_000 + milliseconds));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string FormatTimestamp(TimeSpan value, char fractionalSeparator)
    {
        var hours = (long)value.TotalHours;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours:00}:{value.Minutes:00}:{value.Seconds:00}{fractionalSeparator}{value.Milliseconds:000}");
    }

    private static string SanitizeSubRipText(string source)
    {
        var decoded = WebUtility.HtmlDecode(source);
        var output = new StringBuilder(decoded.Length);
        for (var index = 0; index < decoded.Length; index++)
        {
            if (decoded[index] == '<')
            {
                var end = decoded.IndexOf('>', index + 1);
                if (end >= 0)
                {
                    var tag = decoded[(index + 1)..end].Trim();
                    if (tag is "b" or "/b" or "i" or "/i" or "u" or "/u")
                    {
                        output.Append('<').Append(tag).Append('>');
                    }

                    index = end;
                    continue;
                }
            }

            var character = decoded[index];
            if (!char.IsControl(character) || character is '\n' or '\t')
            {
                output.Append(character);
            }
        }

        return output.ToString().Trim();
    }

    private static bool IsMetadataBlock(string line) =>
        line.Equals("STYLE", StringComparison.Ordinal) ||
        line.Equals("REGION", StringComparison.Ordinal) ||
        line.Equals("NOTE", StringComparison.Ordinal) ||
        line.StartsWith("NOTE ", StringComparison.Ordinal);

    private static Result<T> Failure<T>(string message) =>
        Result<T>.Failure(new TubeForgeError("Caption.InvalidWebVtt", message));

    private sealed record CaptionCue(TimeSpan Start, TimeSpan End, string Text);
}
