using TubeForge.Core.Media;

namespace TubeForge.Transcoding;

public sealed record AudioTranscodeRequest
{
    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public required OutputProfile Output { get; init; }

    public MediaTrimRange? Trim { get; init; }

    public IReadOnlyList<MediaTrimRange> RemovedSegments { get; init; } = [];

    public bool AllowExistingValidatedOutput { get; init; }
}

public sealed record AudioTranscodeReceipt(
    string DestinationPath,
    long BytesWritten,
    int BitrateKbps,
    int Channels,
    int SampleRate);
