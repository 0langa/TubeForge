namespace TubeForge.YouTube.Player;

internal static class JavaScriptStructure
{
    public static int FindMatchingBrace(string source, int openingBrace)
    {
        if (openingBrace < 0 || openingBrace >= source.Length || source[openingBrace] != '{')
        {
            return -1;
        }

        var depth = 0;
        var state = LexicalState.Code;
        var escaped = false;
        for (var index = openingBrace; index < source.Length; index++)
        {
            var current = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';
            switch (state)
            {
                case LexicalState.SingleQuotedString:
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == '\'') state = LexicalState.Code;
                    continue;

                case LexicalState.DoubleQuotedString:
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == '"') state = LexicalState.Code;
                    continue;

                case LexicalState.TemplateString:
                    if (escaped) escaped = false;
                    else if (current == '\\') escaped = true;
                    else if (current == '`') state = LexicalState.Code;
                    continue;

                case LexicalState.LineComment:
                    if (current is '\r' or '\n') state = LexicalState.Code;
                    continue;

                case LexicalState.BlockComment:
                    if (current == '*' && next == '/')
                    {
                        state = LexicalState.Code;
                        index++;
                    }
                    continue;
            }

            if (current == '\'') state = LexicalState.SingleQuotedString;
            else if (current == '"') state = LexicalState.DoubleQuotedString;
            else if (current == '`') state = LexicalState.TemplateString;
            else if (current == '/' && next == '/')
            {
                state = LexicalState.LineComment;
                index++;
            }
            else if (current == '/' && next == '*')
            {
                state = LexicalState.BlockComment;
                index++;
            }
            else if (current == '{') depth++;
            else if (current == '}' && --depth == 0) return index;
        }

        return -1;
    }

    public static IEnumerable<string> TopLevelStatements(string body)
    {
        var start = 0;
        var parentheses = 0;
        var brackets = 0;
        var braces = 0;
        var quote = '\0';
        var escaped = false;
        for (var index = 0; index < body.Length; index++)
        {
            var character = body[index];
            if (quote != '\0')
            {
                if (escaped) escaped = false;
                else if (character == '\\') escaped = true;
                else if (character == quote) quote = '\0';
                continue;
            }

            if (character is '\'' or '"' or '`') quote = character;
            else if (character == '(') parentheses++;
            else if (character == ')') parentheses--;
            else if (character == '[') brackets++;
            else if (character == ']') brackets--;
            else if (character == '{') braces++;
            else if (character == '}') braces--;
            else if (character == ';' && parentheses == 0 && brackets == 0 && braces == 0)
            {
                yield return body[start..index];
                start = index + 1;
            }
        }

        if (start < body.Length)
        {
            yield return body[start..];
        }
    }

    private enum LexicalState
    {
        Code,
        SingleQuotedString,
        DoubleQuotedString,
        TemplateString,
        LineComment,
        BlockComment
    }
}
