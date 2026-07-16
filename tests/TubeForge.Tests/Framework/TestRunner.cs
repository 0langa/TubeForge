using System.Diagnostics;
using System.Reflection;

namespace TubeForge.Tests.Framework;

public static class TestRunner
{
    public static async Task<int> RunAsync(string? filter)
    {
        var tests = Assembly.GetExecutingAssembly()
            .GetTypes()
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Select(method => (Type: type, Method: method, Attribute: method.GetCustomAttribute<TestAttribute>())))
            .Where(test => test.Attribute is not null)
            .Where(test => string.IsNullOrWhiteSpace(filter) ||
                           FullName(test.Type, test.Method, test.Attribute!).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (tests.Length == 0)
        {
            Console.Error.WriteLine("No tests matched.");
            return 2;
        }

        var failures = 0;
        var suiteTimer = Stopwatch.StartNew();
        foreach (var test in tests)
        {
            var name = FullName(test.Type, test.Method, test.Attribute!);
            var timer = Stopwatch.StartNew();
            try
            {
                var result = test.Method.Invoke(null, null);
                if (result is Task task)
                {
                    await task.ConfigureAwait(false);
                }

                Console.WriteLine($"PASS {name} ({timer.ElapsedMilliseconds} ms)");
            }
            catch (TargetInvocationException exception) when (exception.InnerException is not null)
            {
                failures++;
                PrintFailure(name, exception.InnerException);
            }
            catch (Exception exception)
            {
                failures++;
                PrintFailure(name, exception);
            }
        }

        Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed in {suiteTimer.ElapsedMilliseconds} ms");
        return failures == 0 ? 0 : 1;
    }

    private static string FullName(Type type, MethodInfo method, TestAttribute attribute) =>
        attribute.Name ?? $"{type.Name}.{method.Name}";

    private static void PrintFailure(string name, Exception exception)
    {
        Console.Error.WriteLine($"FAIL {name}");
        Console.Error.WriteLine($"  {exception.GetType().Name}: {exception.Message}");
        if (exception is not AssertionException)
        {
            Console.Error.WriteLine(exception.StackTrace);
        }
    }
}
