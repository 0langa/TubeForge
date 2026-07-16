using System.Collections;

namespace TubeForge.Tests.Framework;

public static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new AssertionException(message ?? "Expected condition to be true.");
        }
    }

    public static void False(bool condition, string? message = null) =>
        True(!condition, message ?? "Expected condition to be false.");

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new AssertionException(message ?? $"Expected <{expected}> but received <{actual}>.");
        }
    }

    public static void SequenceEqual(IEnumerable expected, IEnumerable actual, string? message = null)
    {
        var expectedValues = expected.Cast<object?>().ToArray();
        var actualValues = actual.Cast<object?>().ToArray();
        if (!expectedValues.SequenceEqual(actualValues))
        {
            throw new AssertionException(message ??
                $"Expected [{string.Join(", ", expectedValues)}] but received [{string.Join(", ", actualValues)}].");
        }
    }

    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new AssertionException(
                $"Expected {typeof(TException).Name} but received {exception.GetType().Name}.");
        }

        throw new AssertionException($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}

public sealed class AssertionException(string message) : Exception(message);
