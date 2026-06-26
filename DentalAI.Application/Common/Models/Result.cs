namespace DentalAI.Application.Common.Models;

public class Result
{
    public bool Success { get; protected init; }
    public string Message { get; protected init; } = string.Empty;
    public ResultStatus Status { get; protected init; } = ResultStatus.Success;
    public IReadOnlyCollection<string> Errors { get; protected init; } = Array.Empty<string>();

    public static Result Ok(string message = "") => new()
    {
        Success = true,
        Message = message,
        Status = ResultStatus.Success
    };

    public static Result Fail(ResultStatus status, string message, params string[] errors) => new()
    {
        Success = false,
        Message = message,
        Status = status,
        Errors = errors
    };

    public static Result Fail(ResultStatus status, string message, IEnumerable<string> errors) => new()
    {
        Success = false,
        Message = message,
        Status = status,
        Errors = errors.ToArray()
    };
}

public sealed class Result<T> : Result
{
    public T? Data { get; init; }

    public static Result<T> Ok(T data, string message = "") => new()
    {
        Success = true,
        Message = message,
        Status = ResultStatus.Success,
        Data = data
    };

    public static new Result<T> Fail(ResultStatus status, string message, params string[] errors) => new()
    {
        Success = false,
        Message = message,
        Status = status,
        Errors = errors
    };

    public static new Result<T> Fail(ResultStatus status, string message, IEnumerable<string> errors) => new()
    {
        Success = false,
        Message = message,
        Status = status,
        Errors = errors.ToArray()
    };
}
