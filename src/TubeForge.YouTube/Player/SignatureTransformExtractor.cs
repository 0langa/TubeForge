using System.Globalization;
using System.Text.RegularExpressions;

namespace TubeForge.YouTube.Player;

internal static partial class SignatureTransformExtractor
{
    private const int MaximumPlayerScriptLength = 6 * 1024 * 1024;
    private const int MaximumFunctionBodyLength = 8 * 1024;
    private const int MaximumOperations = 32;

    public static IReadOnlyList<SignatureTransformPlan> Extract(string playerScript)
    {
        ArgumentNullException.ThrowIfNull(playerScript);
        if (playerScript.Length > MaximumPlayerScriptLength ||
            !JavaScriptTokenizer.TryTokenize(playerScript, out var tokens))
        {
            return [];
        }

        var plans = new List<SignatureTransformPlan>();
        foreach (var function in JavaScriptFunctionLocator.FindSingleArgumentFunctions(playerScript, tokens))
        {
            var bodyLength = function.BodyEnd - function.BodyStart;
            if (bodyLength <= MaximumFunctionBodyLength &&
                ContainsArrayStringCall(playerScript, tokens, function, "split") &&
                ContainsArrayStringCall(playerScript, tokens, function, "join") &&
                TryParseOperations(
                    playerScript,
                    playerScript[function.BodyStart..function.BodyEnd],
                    function.Argument,
                    out var operations) &&
                operations.Count is > 0 and <= MaximumOperations)
            {
                plans.Add(new SignatureTransformPlan(function.Name, operations));
            }
        }

        return plans
            .DistinctBy(plan => plan.Name)
            .ToArray();
    }

    private static bool ContainsArrayStringCall(
        string source,
        IReadOnlyList<JavaScriptToken> tokens,
        JavaScriptFunction function,
        string method)
    {
        for (var index = function.TokenStart; index + 5 <= function.TokenEnd; index++)
        {
            if (tokens[index].Is(source, function.Argument) &&
                tokens[index + 1].Is(source, ".") &&
                tokens[index + 2].Is(source, method) &&
                tokens[index + 3].Is(source, "(") &&
                IsEmptyString(source, tokens[index + 4]) &&
                tokens[index + 5].Is(source, ")"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEmptyString(string source, JavaScriptToken token) =>
        token.Kind == JavaScriptTokenKind.String &&
        (token.Is(source, "\"\"") || token.Is(source, "''"));

    private static bool TryParseOperations(
        string source,
        string body,
        string argument,
        out IReadOnlyList<SignatureOperation> operations)
    {
        var parsed = new List<SignatureOperation>();
        foreach (var statement in JavaScriptStructure.TopLevelStatements(body))
        {
            if (IsInitializationOrReturnStatement(statement, argument))
            {
                continue;
            }

            if (TryParseDirectOperation(statement, argument, out var directOperation))
            {
                parsed.Add(directOperation);
                continue;
            }

            var callMatch = HelperCallRegex(argument).Match(statement);
            if (!callMatch.Success)
            {
                if (!string.IsNullOrWhiteSpace(statement))
                {
                    return Fail(out operations);
                }

                continue;
            }

            var helper = callMatch.Groups["helper"].Value;
            var method = callMatch.Groups["method"].Success
                ? callMatch.Groups["method"].Value
                : callMatch.Groups["quotedMethod"].Value;
            var numericArgument = callMatch.Groups["number"].Success
                ? int.Parse(callMatch.Groups["number"].Value, CultureInfo.InvariantCulture)
                : 0;
            if (!TryClassifyHelper(source, helper, method, numericArgument, out var operation))
            {
                return Fail(out operations);
            }

            parsed.Add(operation);
        }

        operations = parsed;
        return parsed.Count > 0;
    }

    private static bool IsInitializationOrReturnStatement(string statement, string argument)
    {
        if (!JavaScriptTokenizer.TryTokenize(statement, out var tokens))
        {
            return false;
        }

        if (tokens.Count > 0 && tokens[0].Is(statement, "return"))
        {
            return true;
        }

        for (var index = 0; index + 2 < tokens.Count; index++)
        {
            if (tokens[index].Is(statement, argument) &&
                tokens[index + 1].Is(statement, ".") &&
                (tokens[index + 2].Is(statement, "split") || tokens[index + 2].Is(statement, "join")))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryParseDirectOperation(
        string statement,
        string argument,
        out SignatureOperation operation)
    {
        operation = default;
        var compact = WhitespaceRegex().Replace(statement, string.Empty);
        if (compact.Contains($"{argument}.reverse()", StringComparison.Ordinal))
        {
            operation = new SignatureOperation(SignatureOperationKind.Reverse);
            return true;
        }

        var removeMatch = Regex.Match(
            compact,
            $"{Regex.Escape(argument)}\\.(?:splice\\(0,|slice\\()(?<number>\\d+)\\)",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        if (removeMatch.Success)
        {
            operation = new SignatureOperation(
                SignatureOperationKind.RemoveFirst,
                int.Parse(removeMatch.Groups["number"].Value, CultureInfo.InvariantCulture));
            return true;
        }

        return false;
    }

    private static bool TryClassifyHelper(
        string source,
        string helper,
        string method,
        int argument,
        out SignatureOperation operation)
    {
        operation = default;
        var declaration = HelperDeclarationRegex(helper).Match(source);
        if (!declaration.Success)
        {
            return false;
        }

        var objectStart = source.IndexOf('{', declaration.Index + declaration.Length - 1);
        var objectEnd = JavaScriptStructure.FindMatchingBrace(source, objectStart);
        if (objectStart < 0 || objectEnd < 0 || objectEnd - objectStart > 16 * 1024)
        {
            return false;
        }

        var objectBody = source[(objectStart + 1)..objectEnd];
        var methodMatch = HelperMethodRegex(method).Match(objectBody);
        if (!methodMatch.Success)
        {
            return false;
        }

        var methodBodyStart = objectStart + 1 + methodMatch.Index + methodMatch.Length - 1;
        var methodBodyEnd = JavaScriptStructure.FindMatchingBrace(source, methodBodyStart);
        if (methodBodyEnd < 0 || methodBodyEnd - methodBodyStart > 2_048)
        {
            return false;
        }

        var methodBody = source[methodBodyStart..(methodBodyEnd + 1)];
        if (methodBody.Contains(".reverse(", StringComparison.Ordinal))
        {
            operation = new SignatureOperation(SignatureOperationKind.Reverse);
            return true;
        }

        if (methodBody.Contains(".splice(0,", StringComparison.Ordinal) ||
            methodBody.Contains(".slice(", StringComparison.Ordinal))
        {
            operation = new SignatureOperation(SignatureOperationKind.RemoveFirst, argument);
            return true;
        }

        if (methodBody.Contains("%", StringComparison.Ordinal) &&
            methodBody.Contains(".length", StringComparison.Ordinal) &&
            methodBody.Contains("[0]", StringComparison.Ordinal))
        {
            operation = new SignatureOperation(SignatureOperationKind.Swap, argument);
            return true;
        }

        return false;
    }

    private static bool Fail(out IReadOnlyList<SignatureOperation> operations)
    {
        operations = [];
        return false;
    }

    private static Regex HelperCallRegex(string argument) => new(
        $"(?<helper>[$A-Za-z_][$\\w]*)\\s*(?:\\.\\s*(?<method>[$A-Za-z_][$\\w]*)|" +
        $"\\[\\s*['\\\"](?<quotedMethod>[^'\\\"]+)['\\\"]\\s*\\])\\s*\\(\\s*" +
        $"{Regex.Escape(argument)}\\s*(?:,\\s*(?<number>\\d+))?\\s*\\)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static Regex HelperDeclarationRegex(string helper) => new(
        $@"\b{Regex.Escape(helper)}\s*=\s*\{{",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static Regex HelperMethodRegex(string method) => new(
        $"(?:\\b{Regex.Escape(method)}\\b|['\\\"]{Regex.Escape(method)}['\\\"])" +
        "\\s*(?::\\s*(?:function\\s*)?)?\\([^)]*\\)\\s*(?:=>\\s*)?\\{",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
