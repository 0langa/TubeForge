namespace TubeForge.Core.Media;

public sealed record VideoChapter
{
    public required string Title { get; init; }

    public required TimeSpan StartTime { get; init; }
}
