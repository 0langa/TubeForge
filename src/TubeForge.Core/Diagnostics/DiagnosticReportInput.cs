namespace TubeForge.Core.Diagnostics;

public sealed record DiagnosticReportInput
{
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    public required string ApplicationVersion { get; init; }

    public required string RuntimeDescription { get; init; }

    public required string ProcessArchitecture { get; init; }

    public required string ExtractionStage { get; init; }

    public int TotalFormats { get; init; }

    public int MatchingOutputs { get; init; }

    public int ActiveQueueItems { get; init; }

    public int WaitingQueueItems { get; init; }

    public int PausedQueueItems { get; init; }

    public int CompletedQueueItems { get; init; }

    public int FailedQueueItems { get; init; }

    public int CancelledQueueItems { get; init; }
}
