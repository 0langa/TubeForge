namespace TubeForge.Core.YouTube;

public readonly record struct YouTubeVideoId
{
    public const int RequiredLength = 11;

    private YouTubeVideoId(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryCreate(string? candidate, out YouTubeVideoId videoId)
    {
        videoId = default;
        if (candidate is null || candidate.Length != RequiredLength)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')
            {
                return false;
            }
        }

        videoId = new YouTubeVideoId(candidate);
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
