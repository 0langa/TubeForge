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
        if (playerScript.Length > MaximumPlayerScriptLength)
        {
            return [];
        }

        var plans = new List<SignatureTransformPlan>();
        var searchIndex = 0;
        while ((searchIndex = IndexOfSplitEmpty(playerScript, searchIndex)) >= 0)
        {
            if (TryFindContainingFunction(playerScript, searchIndex, out var name, out var argument, out var body) &&
                body.Length <= MaximumFunctionBodyLength &&
                ContainsJoinEmpty(body, argument) &&
                TryParseOperations(playerScript, body, argument, out var operations) &&
                operations.Count is > 0 and <= MaximumOperations)
            {
                plans.Add(new SignatureTransformPlan(name, operations));
            }

            searchIndex += 8;
        }

        return plans
            .DistinctBy(plan => string.Join(',', plan.Operations))
            .ToArray();
    }

    private static bool TryFindContainingFunction(
        string source,
        int splitIndex,
        out string name,
        out string argument,
        out string body)
    {
        name = string.Empty;
        argument = string.Empty;
        body = string.Empty;
        var searchStart = Math.Max(0, splitIndex - 2_048);
        var functionIndex = source.LastIndexOf("function(", splitIndex, splitIndex - searchStart, StringComparison.Ordinal);
        if (functionIndex < 0)
        {
            return false;
        }

        var parameterStart = functionIndex + "function(".Length;
        var parameterEnd = source.IndexOf(')', parameterStart);
        if (parameterEnd < 0 || parameterEnd > splitIndex)
        {
            return false;
        }

        argument = source[parameterStart..parameterEnd].Trim();
        if (!IdentifierRegex().IsMatch(argument))
        {
            return false;
        }

        var bodyStart = source.IndexOf('{', parameterEnd + 1);
        if (bodyStart < 0 || bodyStart > splitIndex)
        {
            return false;
        }

        var bodyEnd = JavaScriptStructure.FindMatchingBrace(source, bodyStart);
        if (bodyEnd < splitIndex || bodyEnd - bodyStart > MaximumFunctionBodyLength)
        {
            return false;
        }

        var nameEnd = functionIndex;
        var equals = source.LastIndexOf('=', nameEnd - 1, Math.Min(256, nameEnd));
        if (equals >= 0)
        {
            var nameStart = equals - 1;
            while (nameStart >= 0 && IsIdentifierCharacter(source[nameStart])) nameStart--;
            name = source[(nameStart + 1)..equals].Trim();
        }

        if (!IdentifierRegex().IsMatch(name))
        {
            name = $"anonymous@{functionIndex.ToString(CultureInfo.InvariantCulture)}";
        }

        body = source[(bodyStart + 1)..bodyEnd];
        return true;
    }

    private static bool TryParseOperations(
        string source,
        string body,
        string argument,
        out IReadOnlyList<SignatureOperation> operations)
    {
        var parsed = new List<SignatureOperation>();
        foreach (var statement in JavaScriptStructure.TopLevelStatements(body))
        {
            if (statement.Contains($"{argument}.split", StringComparison.Ordinal) ||
                statement.Contains($"{argument}.join", StringComparison.Ordinal) ||
                statement.TrimStart().StartsWith("return", StringComparison.Ordinal))
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

    private static int IndexOfSplitEmpty(string source, int startIndex)
    {
        var doubleQuoted = source.IndexOf(".split(\"\")", startIndex, StringComparison.Ordinal);
        var singleQuoted = source.IndexOf(".split('')", startIndex, StringComparison.Ordinal);
        if (doubleQuoted < 0) return singleQuoted;
        if (singleQuoted < 0) return doubleQuoted;
        return Math.Min(doubleQuoted, singleQuoted);
    }

    private static bool ContainsJoinEmpty(string body, string argument) =>
        body.Contains($"{argument}.join(\"\")", StringComparison.Ordinal) ||
        body.Contains($"{argument}.join('')", StringComparison.Ordinal);

    private static bool IsIdentifierCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '_' or '$';

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
        "\\s*:\\s*function\\s*\\([^)]*\\)\\s*\\{",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    [GeneratedRegex(@"^[$A-Za-z_][$\w]*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
