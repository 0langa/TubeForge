namespace TubeForge.Core.Media;

[Flags]
public enum SponsorBlockCategories
{
    None = 0,
    Sponsor = 1 << 0,
    Intro = 1 << 1,
    Outro = 1 << 2,
    SelfPromotion = 1 << 3,
    Interaction = 1 << 4,
    Preview = 1 << 5,
    Filler = 1 << 6,
    All = Sponsor | Intro | Outro | SelfPromotion | Interaction | Preview | Filler
}

public enum SponsorBlockMode
{
    Chapters,
    Remove
}

public readonly record struct SponsorBlockSelection(
    SponsorBlockMode Mode,
    SponsorBlockCategories Categories)
{
    private static readonly (SponsorBlockCategories Value, string Code)[] CategoryCodes =
    [
        (SponsorBlockCategories.Sponsor, "sponsor"),
        (SponsorBlockCategories.Intro, "intro"),
        (SponsorBlockCategories.Outro, "outro"),
        (SponsorBlockCategories.SelfPromotion, "selfpromo"),
        (SponsorBlockCategories.Interaction, "interaction"),
        (SponsorBlockCategories.Preview, "preview"),
        (SponsorBlockCategories.Filler, "filler")
    ];

    public bool IsValid =>
        Enum.IsDefined(Mode) &&
        Categories != SponsorBlockCategories.None &&
        (Categories & ~SponsorBlockCategories.All) == 0;

    public string Identity
    {
        get
        {
            var selected = Categories;
            return $"{(Mode == SponsorBlockMode.Chapters ? "chapters" : "remove")}." +
                string.Join(',', CategoryCodes
                    .Where(category => selected.HasFlag(category.Value))
                    .Select(category => category.Code));
        }
    }

    public IReadOnlyList<string> ApiCategories
    {
        get
        {
            var selected = Categories;
            return CategoryCodes
                .Where(category => selected.HasFlag(category.Value))
                .Select(category => category.Code)
                .ToArray();
        }
    }

    public static bool TryParseIdentity(string? value, out SponsorBlockSelection selection)
    {
        selection = default;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
        {
            return false;
        }

        var separator = value.IndexOf('.');
        if (separator <= 0 || separator != value.LastIndexOf('.') || separator == value.Length - 1)
        {
            return false;
        }

        var mode = value[..separator] switch
        {
            "chapters" => SponsorBlockMode.Chapters,
            "remove" => SponsorBlockMode.Remove,
            _ => (SponsorBlockMode?)null
        };
        if (mode is null)
        {
            return false;
        }

        var categories = SponsorBlockCategories.None;
        foreach (var code in value[(separator + 1)..].Split(','))
        {
            var category = CategoryCodes.FirstOrDefault(candidate => candidate.Code == code);
            if (category.Value == SponsorBlockCategories.None || categories.HasFlag(category.Value))
            {
                return false;
            }

            categories |= category.Value;
        }

        selection = new SponsorBlockSelection(mode.Value, categories);
        return selection.IsValid && selection.Identity == value;
    }
}

public sealed record SponsorBlockSegment(
    TimeSpan Start,
    TimeSpan End,
    string Category,
    string Description)
{
    public bool IsValid =>
        Start >= TimeSpan.Zero && End > Start && End <= MediaTrimRange.MaximumEnd &&
        !string.IsNullOrWhiteSpace(Category) && Category.Length <= 32 &&
        Description.Length <= 500 && !Description.Any(char.IsControl);
}
