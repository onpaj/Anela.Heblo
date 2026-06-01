namespace Anela.Heblo.Domain.Shared;

public class Result<T>
{
    public bool IsSuccess { get; private set; }
    public string? ErrorMessage { get; private set; }
    public T? Value { get; private set; }

    private Result(bool isSuccess, T? value, string? errorMessage = null)
    {
        IsSuccess = isSuccess;
        Value = value;
        ErrorMessage = errorMessage;
    }

    public static Result<T> Success(T value) => new(true, value);

    public static Result<T> Failure(string errorMessage) => new(false, default, errorMessage);

    public static implicit operator bool(Result<T> result) => result.IsSuccess;
}

public static class Result
{
    public static Result<T> Success<T>(T value) => Result<T>.Success(value);

    public static Result<T> Failure<T>(string errorMessage) => Result<T>.Failure(errorMessage);
}