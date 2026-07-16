namespace TubeForge.YouTube.Player;

internal sealed record YouTubeClientProfile(
    string Name,
    string NumericId,
    string Version,
    string UserAgent)
{
    public static YouTubeClientProfile Android { get; } = new(
        "ANDROID",
        "3",
        "20.10.38",
        "com.google.android.youtube/20.10.38 (Linux; U; Android 14)");
}
