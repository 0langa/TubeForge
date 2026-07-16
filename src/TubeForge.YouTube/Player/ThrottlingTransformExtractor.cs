namespace TubeForge.YouTube.Player;

internal static class ThrottlingTransformExtractor
{
    private const int MaximumCallSiteTokens = 512;

    public static IReadOnlyList<SignatureTransformPlan> Extract(
        string playerScript,
        IReadOnlyList<SignatureTransformPlan> candidates)
    {
        ArgumentNullException.ThrowIfNull(playerScript);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0 ||
            !JavaScriptTokenizer.TryTokenize(playerScript, out var tokens))
        {
            return [];
        }

        var candidatesByName = candidates
            .Where(candidate => !candidate.Name.StartsWith("anonymous@", StringComparison.Ordinal))
            .GroupBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var arrays = ReadFunctionArrays(playerScript, tokens, candidatesByName);
        var matches = new List<SignatureTransformPlan>();
        for (var index = 0; index + 4 < tokens.Count; index++)
        {
            if (!tokens[index].Is(playerScript, ".") ||
                !tokens[index + 1].Is(playerScript, "get") ||
                !tokens[index + 2].Is(playerScript, "(") ||
                !IsNString(playerScript, tokens[index + 3]) ||
                !tokens[index + 4].Is(playerScript, ")"))
            {
                continue;
            }

            var end = Math.Min(tokens.Count - 1, index + MaximumCallSiteTokens);
            for (var call = index + 5; call <= end; call++)
            {
                if (IsSetNCall(playerScript, tokens, call))
                {
                    break;
                }

                if (TryResolveDirectCall(playerScript, tokens, call, candidatesByName, out var direct) ||
                    TryResolveArrayCall(playerScript, tokens, call, arrays, candidatesByName, out direct))
                {
                    matches.Add(direct);
                    break;
                }
            }
        }

        return matches.DistinctBy(match => match.Name).ToArray();
    }

    private static Dictionary<string, string[]> ReadFunctionArrays(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        IReadOnlyDictionary<string, SignatureTransformPlan> candidates)
    {
        var arrays = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var index = 0; index + 4 < tokens.Count; index++)
        {
            if (tokens[index].Kind != JavaScriptTokenKind.Identifier ||
                !tokens[index + 1].Is(source, "=") ||
                !tokens[index + 2].Is(source, "["))
            {
                continue;
            }

            var names = new List<string>();
            var cursor = index + 3;
            while (cursor < tokens.Count && !tokens[cursor].Is(source, "]") && names.Count < 64)
            {
                if (tokens[cursor].Kind == JavaScriptTokenKind.Identifier)
                {
                    var name = tokens[cursor].Text(source);
                    if (!candidates.ContainsKey(name))
                    {
                        names.Clear();
                        break;
                    }

                    names.Add(name);
                    cursor++;
                    if (cursor < tokens.Count && tokens[cursor].Is(source, ",")) cursor++;
                }
                else
                {
                    names.Clear();
                    break;
                }
            }

            if (names.Count > 0 && cursor < tokens.Count && tokens[cursor].Is(source, "]"))
            {
                arrays[tokens[index].Text(source)] = [.. names];
            }
        }

        return arrays;
    }

    private static bool TryResolveDirectCall(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        int index,
        IReadOnlyDictionary<string, SignatureTransformPlan> candidates,
        out SignatureTransformPlan plan)
    {
        plan = null!;
        return index + 1 < tokens.Count &&
               tokens[index].Kind == JavaScriptTokenKind.Identifier &&
               tokens[index + 1].Is(source, "(") &&
               candidates.TryGetValue(tokens[index].Text(source), out plan!);
    }

    private static bool TryResolveArrayCall(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        int index,
        IReadOnlyDictionary<string, string[]> arrays,
        IReadOnlyDictionary<string, SignatureTransformPlan> candidates,
        out SignatureTransformPlan plan)
    {
        plan = null!;
        if (index + 4 >= tokens.Count ||
            tokens[index].Kind != JavaScriptTokenKind.Identifier ||
            !tokens[index + 1].Is(source, "[") ||
            tokens[index + 2].Kind != JavaScriptTokenKind.Number ||
            !int.TryParse(tokens[index + 2].Text(source), out var member) ||
            !tokens[index + 3].Is(source, "]") ||
            !tokens[index + 4].Is(source, "(") ||
            !arrays.TryGetValue(tokens[index].Text(source), out var names) ||
            member < 0 || member >= names.Length)
        {
            return false;
        }

        return candidates.TryGetValue(names[member], out plan!);
    }

    private static bool IsSetNCall(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        int index) =>
        index + 4 < tokens.Count &&
        tokens[index].Is(source, ".") &&
        tokens[index + 1].Is(source, "set") &&
        tokens[index + 2].Is(source, "(") &&
        IsNString(source, tokens[index + 3]);

    private static bool IsNString(string source, JavaScriptToken token) =>
        token.Kind == JavaScriptTokenKind.String &&
        (token.Is(source, "\"n\"") || token.Is(source, "'n'"));
}
