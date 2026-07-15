namespace InternalResourceStore.Application;

public enum ErrorType
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict
}

public sealed record ApplicationError(ErrorType Type, string Code, string Message);

public sealed class Result<T>
{
    private Result(T value)
    {
        Value = value;
    }

    private Result(ApplicationError error)
    {
        Error = error;
    }

    public T? Value { get; }
    public ApplicationError? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result<T> Success(T value) => new(value);
    public static Result<T> Failure(ApplicationError error) => new(error);
}

public sealed class Result
{
    private Result(ApplicationError? error)
    {
        Error = error;
    }

    public ApplicationError? Error { get; }
    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);
    public static Result Failure(ApplicationError error) => new(error);
}
