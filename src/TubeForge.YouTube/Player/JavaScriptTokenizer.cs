namespace TubeForge.YouTube.Player;

internal enum JavaScriptTokenKind
{
    Identifier,
    Number,
    String,
    RegularExpression,
    Punctuator
}

internal readonly record struct JavaScriptToken(
    JavaScriptTokenKind Kind,
    int Start,
    int Length)
{
    public bool Is(string source, string value) =>
        Length == value.Length &&
        source.AsSpan(Start, Length).SequenceEqual(value);

    public string Text(string source) => source.Substring(Start, Length);
}

internal static class JavaScriptTokenizer
{
    internal const int MaximumSourceLength = 6 * 1024 * 1024;
    private const int MaximumTokenLength = 64 * 1024;
    private const int MaximumTokens = 2_000_000;

    private static readonly string[] MultiCharacterPunctuators =
    [
        ">>>=", "===", "!==", ">>>", "**=", "<<=", ">>=", "...", "=>", "==", "!=",
        "<=", ">=", "++", "--", "&&", "||", "??", "+=", "-=", "*=", "/=", "%=",
        "&=", "|=", "^=", "<<", ">>", "**", "?."
    ];

    public static bool TryTokenize(string? source, out IReadOnlyList<JavaScriptToken> tokens)
        => TryTokenize(source, out tokens, out _);

    internal static bool TryTokenize(
        string? source,
        out IReadOnlyList<JavaScriptToken> tokens,
        out int errorIndex)
    {
        tokens = [];
        errorIndex = -1;
        if (source is null || source.Length > MaximumSourceLength)
        {
            errorIndex = source?.Length ?? 0;
            return false;
        }

        var output = new List<JavaScriptToken>(Math.Min(source.Length / 3, 64 * 1024));
        var index = 0;
        while (index < source.Length)
        {
            if (char.IsWhiteSpace(source[index]))
            {
                index++;
                continue;
            }

            if (source[index] == '/' && index + 1 < source.Length)
            {
                if (source[index + 1] == '/')
                {
                    index += 2;
                    while (index < source.Length && source[index] is not '\r' and not '\n') index++;
                    continue;
                }

                if (source[index + 1] == '*')
                {
                    var end = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    if (end < 0)
                    {
                        errorIndex = index;
                        return false;
                    }

                    index = end + 2;
                    continue;
                }
            }

            var start = index;
            var current = source[index];
            JavaScriptTokenKind kind;
            if (IsIdentifierStart(current))
            {
                kind = JavaScriptTokenKind.Identifier;
                index++;
                while (index < source.Length && IsIdentifierPart(source[index])) index++;
            }
            else if (char.IsAsciiDigit(current))
            {
                kind = JavaScriptTokenKind.Number;
                index++;
                while (index < source.Length && IsNumberPart(source[index])) index++;
            }
            else if (current is '\'' or '"' or '`')
            {
                kind = JavaScriptTokenKind.String;
                if (!TrySkipString(source, current, ref index))
                {
                    errorIndex = start;
                    return false;
                }
            }
            else if (current == '/' && CanStartRegularExpression(source, output))
            {
                kind = JavaScriptTokenKind.RegularExpression;
                if (!TrySkipRegularExpression(source, ref index))
                {
                    errorIndex = start;
                    return false;
                }
            }
            else
            {
                kind = JavaScriptTokenKind.Punctuator;
                index += PunctuatorLength(source, index);
            }

            var length = index - start;
            if (length > MaximumTokenLength || output.Count >= MaximumTokens)
            {
                errorIndex = start;
                return false;
            }

            output.Add(new JavaScriptToken(kind, start, length));
        }

        tokens = output;
        return true;
    }

    private static bool TrySkipString(string source, char quote, ref int index)
    {
        var start = index++;
        var escaped = false;
        while (index < source.Length && index - start <= MaximumTokenLength)
        {
            var current = source[index++];
            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\')
            {
                escaped = true;
            }
            else if (current == quote)
            {
                return true;
            }
            else if (quote != '`' && current is ('\r' or '\n'))
            {
                return false;
            }
        }

        return false;
    }

    private static int PunctuatorLength(string source, int index)
    {
        foreach (var punctuator in MultiCharacterPunctuators)
        {
            if (source.AsSpan(index).StartsWith(punctuator, StringComparison.Ordinal))
            {
                return punctuator.Length;
            }
        }

        return 1;
    }

    private static bool TrySkipRegularExpression(string source, ref int index)
    {
        var start = index++;
        var escaped = false;
        var inCharacterClass = false;
        while (index < source.Length && index - start <= MaximumTokenLength)
        {
            var current = source[index++];
            if (escaped)
            {
                escaped = false;
            }
            else if (current == '\\')
            {
                escaped = true;
            }
            else if (current is '\r' or '\n')
            {
                return false;
            }
            else if (current == '[')
            {
                inCharacterClass = true;
            }
            else if (current == ']' && inCharacterClass)
            {
                inCharacterClass = false;
            }
            else if (current == '/' && !inCharacterClass)
            {
                while (index < source.Length && IsIdentifierPart(source[index])) index++;
                return true;
            }
        }

        return false;
    }

    private static bool CanStartRegularExpression(
        string source,
        IReadOnlyList<JavaScriptToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return true;
        }

        var previous = tokens[^1];
        if (previous.Kind == JavaScriptTokenKind.Identifier)
        {
            return previous.Is(source, "return") ||
                   previous.Is(source, "throw") ||
                   previous.Is(source, "case") ||
                   previous.Is(source, "delete") ||
                   previous.Is(source, "void") ||
                   previous.Is(source, "typeof") ||
                   previous.Is(source, "instanceof") ||
                   previous.Is(source, "in") ||
                   previous.Is(source, "of") ||
                   previous.Is(source, "yield") ||
                   previous.Is(source, "await") ||
                   previous.Is(source, "else") ||
                   previous.Is(source, "do");
        }

        return previous.Kind == JavaScriptTokenKind.Punctuator &&
               (previous.Is(source, "(") ||
                previous.Is(source, "{") ||
                previous.Is(source, "[") ||
                previous.Is(source, ",") ||
                previous.Is(source, ";") ||
                previous.Is(source, ":") ||
                previous.Is(source, "=") ||
                previous.Is(source, "!") ||
                previous.Is(source, "?") ||
                previous.Is(source, "&&") ||
                previous.Is(source, "||") ||
                previous.Is(source, "??") ||
                previous.Is(source, "=>"));
    }

    private static bool IsIdentifierStart(char character) =>
        character is '_' or '$' || char.IsLetter(character);

    private static bool IsIdentifierPart(char character) =>
        IsIdentifierStart(character) || char.IsAsciiDigit(character);

    private static bool IsNumberPart(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '-';
}
