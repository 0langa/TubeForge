namespace TubeForge.Downloads;

public sealed record DiskSpaceForecast
{
    public required long RequiredAdditionalBytes { get; init; }

    public long? AvailableBytes { get; init; }

    public bool IsKnown => AvailableBytes is not null;
}
