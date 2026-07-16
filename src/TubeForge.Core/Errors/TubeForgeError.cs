namespace TubeForge.Core.Errors;

public sealed record TubeForgeError(
    string Code,
    string Message,
    string? TechnicalDetail = null,
    bool IsTransient = false)
{
    public static TubeForgeError InvalidUrl(string message) =>
        new("Input.InvalidUrl", message);

    public static TubeForgeError UnsupportedUrl(string message) =>
        new("Input.UnsupportedUrl", message);
}
