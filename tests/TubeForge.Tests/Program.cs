using TubeForge.Tests.Framework;

var filter = args.Length == 0 || args.Contains("--all", StringComparer.OrdinalIgnoreCase)
    ? null
    : args.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));

return await TestRunner.RunAsync(filter);
