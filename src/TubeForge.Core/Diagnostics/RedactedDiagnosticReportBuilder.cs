using System.Text.Json;

namespace TubeForge.Core.Diagnostics;

public static class RedactedDiagnosticReportBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string Build(DiagnosticReportInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateCount(input.TotalFormats, nameof(input.TotalFormats));
        ValidateCount(input.MatchingOutputs, nameof(input.MatchingOutputs));
        ValidateCount(input.ActiveQueueItems, nameof(input.ActiveQueueItems));
        ValidateCount(input.WaitingQueueItems, nameof(input.WaitingQueueItems));
        ValidateCount(input.PausedQueueItems, nameof(input.PausedQueueItems));
        ValidateCount(input.CompletedQueueItems, nameof(input.CompletedQueueItems));
        ValidateCount(input.FailedQueueItems, nameof(input.FailedQueueItems));
        ValidateCount(input.CancelledQueueItems, nameof(input.CancelledQueueItems));

        var report = new
        {
            schemaVersion = 1,
            generatedAtUtc = input.GeneratedAtUtc,
            application = new
            {
                version = SafeValue(input.ApplicationVersion, 64),
                runtime = SafeValue(input.RuntimeDescription, 128),
                architecture = SafeValue(input.ProcessArchitecture, 32),
                thirdPartyDependencies = false
            },
            extraction = new
            {
                stage = SafeValue(input.ExtractionStage, 64),
                totalFormats = input.TotalFormats,
                matchingOutputs = input.MatchingOutputs
            },
            queue = new
            {
                active = input.ActiveQueueItems,
                waiting = input.WaitingQueueItems,
                paused = input.PausedQueueItems,
                completed = input.CompletedQueueItems,
                failed = input.FailedQueueItems,
                cancelled = input.CancelledQueueItems
            },
            persistence = new
            {
                settingsSchemaVersion = 1,
                queueSchemaVersion = 1
            },
            exclusions = new[]
            {
                "urls", "videoIds", "titles", "channels", "localPaths", "headers", "cookies",
                "signatures", "visitorData", "media"
            }
        };
        return JsonSerializer.Serialize(report, SerializerOptions);
    }

    private static string SafeValue(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) &&
                                   character is not ' ' and not '.' and not '-' and not '_' and not '(' and not ')'))
        {
            return "redacted";
        }

        return value;
    }

    private static void ValidateCount(int count, string parameterName)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
