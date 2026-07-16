using System.Globalization;
using System.Text;
using TubeForge.Core.Errors;
using TubeForge.Core.Results;

namespace TubeForge.Core.Files;

public sealed record FileNameTemplateContext
{
    public required string Title { get; init; }

    public required string Channel { get; init; }

    public required string VideoId { get; init; }

    public required string Quality { get; init; }

    public required string Container { get; init; }

    public int? Index { get; init; }

    public int IndexWidth { get; init; } = 2;
}

public static class FileNameTemplate
{
    public const string Default = "{title}";
    public const int MaximumTemplateLength = 256;
    private const int MaximumRenderedLength = 2_048;

    public static Result<string> Render(string? template, FileNameTemplateContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(template) ||
            template.Length > MaximumTemplateLength ||
            template.Any(char.IsControl) ||
            context.IndexWidth is < 1 or > 8)
        {
            return Invalid("The filename template is empty or exceeds its safe limits.");
        }

        var builder = new StringBuilder(Math.Min(MaximumRenderedLength, template.Length + context.Title.Length));
        for (var position = 0; position < template.Length; position++)
        {
            var current = template[position];
            if (current == '{' && position + 1 < template.Length && template[position + 1] == '{')
            {
                builder.Append('{');
                position++;
                continue;
            }

            if (current == '}' && position + 1 < template.Length && template[position + 1] == '}')
            {
                builder.Append('}');
                position++;
                continue;
            }

            if (current != '{')
            {
                if (current == '}')
                {
                    return Invalid("The filename template contains an unmatched closing brace.");
                }

                builder.Append(current);
                if (builder.Length > MaximumRenderedLength)
                {
                    return Invalid("The rendered filename exceeds the safe length limit.");
                }

                continue;
            }

            var closing = template.IndexOf('}', position + 1);
            if (closing < 0)
            {
                return Invalid("The filename template contains an unmatched opening brace.");
            }

            var token = template[(position + 1)..closing];
            var value = TokenValue(token, context);
            if (value is null)
            {
                return Invalid($"The filename token '{{{token}}}' is not supported.");
            }

            builder.Append(value);
            if (builder.Length > MaximumRenderedLength)
            {
                return Invalid("The rendered filename exceeds the safe length limit.");
            }

            position = closing;
        }

        return string.IsNullOrWhiteSpace(builder.ToString())
            ? Invalid("The filename template produced an empty name.")
            : Result<string>.Success(builder.ToString());
    }

    public static bool IsValid(string? template) => Render(template, new FileNameTemplateContext
    {
        Title = "title",
        Channel = "channel",
        VideoId = "Video000001",
        Quality = "1080p",
        Container = "mp4",
        Index = 1
    }).IsSuccess;

    private static string? TokenValue(string token, FileNameTemplateContext context) => token switch
    {
        "title" => context.Title,
        "channel" => context.Channel,
        "videoId" => context.VideoId,
        "quality" => context.Quality,
        "container" => context.Container,
        "index" => context.Index?.ToString(
            "D" + context.IndexWidth.ToString(CultureInfo.InvariantCulture),
            CultureInfo.InvariantCulture) ?? string.Empty,
        _ => null
    };

    private static Result<string> Invalid(string detail) =>
        Result<string>.Failure(new TubeForgeError(
            "FileName.InvalidTemplate",
            "The filename template is invalid.",
            detail));
}
