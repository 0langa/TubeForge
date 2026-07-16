using TubeForge.Core.Errors;

namespace TubeForge.Core.Results;

public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Error = null;
        IsSuccess = true;
    }

    private Result(TubeForgeError error)
    {
        _value = default;
        Error = error;
        IsSuccess = false;
    }

    public bool IsSuccess { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result has no value.");

    public TubeForgeError? Error { get; }

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(TubeForgeError error) => new(error);
}
