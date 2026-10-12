namespace Bankomat.Domain.Common;

public class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error Error { get; }

    protected Result(bool success, Error error)
    {
        if (success && error != Error.None)
            throw new InvalidOperationException("Success result cannot carry error");
        if (!success && error == Error.None)
            throw new InvalidOperationException("Failure result must carry error");

        IsSuccess = success;
        Error = error;
    }

    public static Result Success() => new(true, Error.None);
    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value, true, Error.None);
    public static Result<T> Failure<T>(Error error) => new(default!, false, error);

    // Только для не-дженерик Result
    public static implicit operator Result(Error error) => Failure(error);
}

public class Result<T> : Result
{
    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot access value of failure result");

    internal Result(T? value, bool success, Error error) : base(success, error)
    {
        _value = value;
    }

    public static implicit operator Result<T>(T value) => Success(value);
    public static implicit operator Result<T>(Error error) => Failure<T>(error);
}