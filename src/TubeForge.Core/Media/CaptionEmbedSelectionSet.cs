namespace TubeForge.Core.Media;

public readonly record struct CaptionEmbedSelectionSet(string Identity)
{
    public const int MaximumTracks = 8;

    public bool IsValid => TryRead(Identity, out _);

    public IReadOnlyList<CaptionEmbedSelection> Selections =>
        TryRead(Identity, out var selections) ? selections : [];

    public static implicit operator CaptionEmbedSelectionSet(CaptionEmbedSelection selection) =>
        new(selection.Identity);

    public static bool TryCreate(
        IEnumerable<CaptionEmbedSelection> selections,
        out CaptionEmbedSelectionSet set)
    {
        ArgumentNullException.ThrowIfNull(selections);
        set = default;
        var items = selections.ToArray();
        if (items.Length is < 1 or > MaximumTracks || items.Any(item => !item.IsValid) ||
            items.Select(item => item.Identity).Distinct(StringComparer.Ordinal).Count() != items.Length)
        {
            return false;
        }

        set = new CaptionEmbedSelectionSet(string.Join(',', items.Select(item => item.Identity)));
        return true;
    }

    public static bool TryParseIdentity(string? value, out CaptionEmbedSelectionSet set)
    {
        set = default;
        if (!TryRead(value, out var selections))
        {
            return false;
        }

        set = new CaptionEmbedSelectionSet(string.Join(',', selections.Select(item => item.Identity)));
        return string.Equals(value, set.Identity, StringComparison.Ordinal);
    }

    private static bool TryRead(
        string? value,
        out IReadOnlyList<CaptionEmbedSelection> selections)
    {
        selections = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var identities = value.Split(',', StringSplitOptions.None);
        if (identities.Length is < 1 or > MaximumTracks)
        {
            return false;
        }

        var parsed = new CaptionEmbedSelection[identities.Length];
        var unique = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < identities.Length; index++)
        {
            if (!CaptionEmbedSelection.TryParseIdentity(identities[index], out parsed[index]) ||
                !unique.Add(parsed[index].Identity))
            {
                return false;
            }
        }

        selections = parsed;
        return true;
    }
}
