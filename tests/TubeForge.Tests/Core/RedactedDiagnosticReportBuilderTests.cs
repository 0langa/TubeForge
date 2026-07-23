using TubeForge.Core.Diagnostics;
using TubeForge.Tests.Framework;

namespace TubeForge.Tests.Core;

public static class RedactedDiagnosticReportBuilderTests
{
    [Test]
    public static void EmitsOnlyWhitelistedTechnicalState()
    {
        var report = RedactedDiagnosticReportBuilder.Build(new DiagnosticReportInput
        {
            GeneratedAtUtc = new DateTimeOffset(2026, 7, 16, 12, 0, 0, TimeSpan.Zero),
            ApplicationVersion = "1.2.3",
            RuntimeDescription = ".NET 10.0.10",
            ProcessArchitecture = "X64",
            ExtractionStage = "AndroidClientResolved",
            TotalFormats = 27,
            MatchingOutputs = 23,
            ActiveQueueItems = 1,
            WaitingQueueItems = 2,
            PausedQueueItems = 3,
            CompletedQueueItems = 4,
            FailedQueueItems = 5,
            ProxyMode = "Manual"
        });

        Assert.True(report.Contains("\"thirdPartyDependencies\": true", StringComparison.Ordinal));
        Assert.True(report.Contains("\"thirdPartyManagedPackages\": false", StringComparison.Ordinal));
        Assert.True(report.Contains("\"bundledFfmpeg\": true", StringComparison.Ordinal));
        Assert.True(report.Contains("\"stage\": \"AndroidClientResolved\"", StringComparison.Ordinal));
        Assert.True(report.Contains("\"totalFormats\": 27", StringComparison.Ordinal));
        Assert.True(report.Contains("\"proxyMode\": \"Manual\"", StringComparison.Ordinal));
        Assert.False(report.Contains("http", StringComparison.OrdinalIgnoreCase));
        Assert.False(report.Contains("C:\\", StringComparison.Ordinal));
        Assert.False(report.Contains("dQw4w9WgXcQ", StringComparison.Ordinal));
    }

    [Test]
    public static void RedactsUnsafeStringsAndRejectsImpossibleCounts()
    {
        var input = new DiagnosticReportInput
        {
            GeneratedAtUtc = DateTimeOffset.UnixEpoch,
            ApplicationVersion = "1.0\nhttps://sensitive.invalid",
            RuntimeDescription = ".NET 10",
            ProcessArchitecture = "X64",
            ExtractionStage = "C:\\private\\video",
            TotalFormats = 1
        };

        var report = RedactedDiagnosticReportBuilder.Build(input);

        Assert.False(report.Contains("sensitive", StringComparison.Ordinal));
        Assert.False(report.Contains("private", StringComparison.Ordinal));
        Assert.True(report.Contains("\"redacted\"", StringComparison.Ordinal));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RedactedDiagnosticReportBuilder.Build(input with { FailedQueueItems = -1 }));
    }
}
