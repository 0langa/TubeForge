namespace TubeForge.YouTube.Player;

internal readonly record struct JavaScriptFunction(
    string Name,
    string Argument,
    int BodyStart,
    int BodyEnd,
    int TokenStart,
    int TokenEnd);

internal static class JavaScriptFunctionLocator
{
    public static IReadOnlyList<JavaScriptFunction> FindSingleArgumentFunctions(
        string source,
        IReadOnlyList<JavaScriptToken> tokens)
    {
        var functions = new List<JavaScriptFunction>();
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index].Is(source, "function"))
            {
                if (TryReadClassic(source, tokens, index, out var function))
                {
                    functions.Add(function);
                }
            }
            else if (tokens[index].Is(source, "=>") &&
                     TryReadArrow(source, tokens, index, out var function))
            {
                functions.Add(function);
            }
        }

        return functions;
    }

    private static bool TryReadClassic(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        int functionIndex,
        out JavaScriptFunction function)
    {
        function = default;
        var parameterOpen = functionIndex + 1;
        string? declaredName = null;
        if (parameterOpen < tokens.Count && tokens[parameterOpen].Kind == JavaScriptTokenKind.Identifier)
        {
            declaredName = tokens[parameterOpen].Text(source);
            parameterOpen++;
        }

        if (!Is(tokens, source, parameterOpen, "(") ||
            !TryReadOneParameter(source, tokens, parameterOpen, out var argument, out var parameterClose) ||
            !Is(tokens, source, parameterClose + 1, "{") ||
            !TryFindMatching(tokens, source, parameterClose + 1, "{", "}", out var bodyClose))
        {
            return false;
        }

        var name = declaredName ?? FindAssignedName(source, tokens, functionIndex - 1);
        function = Create(name, argument, tokens, parameterClose + 1, bodyClose, functionIndex);
        return true;
    }

    private static bool TryReadArrow(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        int arrowIndex,
        out JavaScriptFunction function)
    {
        function = default;
        if (!Is(tokens, source, arrowIndex + 1, "{"))
        {
            return false;
        }

        string argument;
        int parameterStart;
        if (arrowIndex > 0 && tokens[arrowIndex - 1].Kind == JavaScriptTokenKind.Identifier)
        {
            argument = tokens[arrowIndex - 1].Text(source);
            parameterStart = arrowIndex - 1;
        }
        else if (Is(tokens, source, arrowIndex - 1, ")") &&
                 TryFindMatchingBackward(tokens, source, arrowIndex - 1, "(", ")", out var open) &&
                 TryReadOneParameter(source, tokens, open, out argument, out var close) &&
                 close == arrowIndex - 1)
        {
            parameterStart = open;
        }
        else
        {
            return false;
        }

        if (!TryFindMatching(tokens, source, arrowIndex + 1, "{", "}", out var bodyClose))
        {
            return false;
        }

        var name = FindAssignedName(source, tokens, parameterStart - 1);
        function = Create(name, argument, tokens, arrowIndex + 1, bodyClose, parameterStart);
        return true;
    }

    private static JavaScriptFunction Create(
        string name,
        string argument,
        IReadOnlyList<JavaScriptToken> tokens,
        int bodyOpen,
        int bodyClose,
        int tokenStart) => new(
            name,
            argument,
            tokens[bodyOpen].Start + tokens[bodyOpen].Length,
            tokens[bodyClose].Start,
            tokenStart,
            bodyClose);

    private static bool TryReadOneParameter(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        int parameterOpen,
        out string argument,
        out int parameterClose)
    {
        argument = string.Empty;
        parameterClose = -1;
        if (!Is(tokens, source, parameterOpen, "(") ||
            parameterOpen + 2 >= tokens.Count ||
            tokens[parameterOpen + 1].Kind != JavaScriptTokenKind.Identifier ||
            !Is(tokens, source, parameterOpen + 2, ")"))
        {
            return false;
        }

        argument = tokens[parameterOpen + 1].Text(source);
        parameterClose = parameterOpen + 2;
        return true;
    }

    private static string FindAssignedName(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        int beforeIndex)
    {
        if (Is(tokens, source, beforeIndex, "=") && beforeIndex > 0 &&
            tokens[beforeIndex - 1].Kind == JavaScriptTokenKind.Identifier)
        {
            return tokens[beforeIndex - 1].Text(source);
        }

        return $"anonymous@{(beforeIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    private static bool TryFindMatching(
        IReadOnlyList<JavaScriptToken> tokens,
        string source,
        int openingIndex,
        string opening,
        string closing,
        out int closingIndex)
    {
        closingIndex = -1;
        var depth = 0;
        for (var index = openingIndex; index < tokens.Count; index++)
        {
            if (tokens[index].Is(source, opening)) depth++;
            else if (tokens[index].Is(source, closing) && --depth == 0)
            {
                closingIndex = index;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindMatchingBackward(
        IReadOnlyList<JavaScriptToken> tokens,
        string source,
        int closingIndex,
        string opening,
        string closing,
        out int openingIndex)
    {
        openingIndex = -1;
        var depth = 0;
        for (var index = closingIndex; index >= 0; index--)
        {
            if (tokens[index].Is(source, closing)) depth++;
            else if (tokens[index].Is(source, opening) && --depth == 0)
            {
                openingIndex = index;
                return true;
            }
        }

        return false;
    }

    private static bool Is(
        IReadOnlyList<JavaScriptToken> tokens,
        string source,
        int index,
        string value) =>
        index >= 0 && index < tokens.Count && tokens[index].Is(source, value);
}
