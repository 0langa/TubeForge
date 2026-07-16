using TubeForge.Core.Media;

namespace TubeForge.YouTube.Extraction;

public sealed record WatchPageData(
    VideoMetadata Metadata,
    Uri? PlayerScriptUrl,
    int CipheredFormatCount,
    string PlayabilityStatus,
    ExtractionDiagnostics? Diagnostics = null);

public sealed record ExtractionDiagnostics(
    string Stage,
    int TransformPlanCount = 0,
    int ProbeAttemptCount = 0);
