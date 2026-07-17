namespace TubeForge.YouTube.Player;

internal sealed record YouTubeClientProfile(
    string Name,
    string NumericId,
    string Version,
    string UserAgent,
    int? AndroidSdkVersion = null,
    string? DeviceMake = null,
    string? DeviceModel = null,
    string? OsName = null,
    string? OsVersion = null,
    bool IsEmbedded = false)
{
    public static YouTubeClientProfile AndroidVr { get; } = new(
        "ANDROID_VR",
        "28",
        "1.65.10",
        "com.google.android.apps.youtube.vr.oculus/1.65.10 " +
        "(Linux; U; Android 12L; eureka-user Build/SQ3A.220605.009.A1) gzip",
        AndroidSdkVersion: 32,
        DeviceMake: "Oculus",
        DeviceModel: "Quest 3",
        OsName: "Android",
        OsVersion: "12L");

    public static YouTubeClientProfile WebEmbedded { get; } = new(
        "WEB_EMBEDDED_PLAYER",
        "56",
        "2.20260708.00.00",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36",
        IsEmbedded: true);

    public static YouTubeClientProfile Tv { get; } = new(
        "TVHTML5",
        "7",
        "7.20260707.07.00",
        "Mozilla/5.0 (ChromiumStylePlatform) Cobalt/25.lts.30.1034943-gold " +
        "(unlike Gecko), Unknown_TV_Unknown_0/Unknown (Unknown, Unknown)");

    public static YouTubeClientProfile Android { get; } = new(
        "ANDROID",
        "3",
        "21.26.364",
        "com.google.android.youtube/21.26.364 (Linux; U; Android 11) gzip",
        AndroidSdkVersion: 30,
        OsName: "Android",
        OsVersion: "11");
}
