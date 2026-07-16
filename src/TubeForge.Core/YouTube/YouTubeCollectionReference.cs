namespace TubeForge.Core.YouTube;

public enum YouTubeCollectionKind
{
    Playlist,
    Channel
}

public sealed record YouTubeCollectionReference
{
    public required YouTubeCollectionKind Kind { get; init; }

    public required string Identifier { get; init; }

    public required Uri CanonicalUrl { get; init; }
}
