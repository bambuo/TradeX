namespace TradeX.Application.Common;

/// <summary>
/// 统一应用层返回结果。封装成功/失败状态码与消息。
/// </summary>
public sealed class Result<T>
{
    private Result(bool success, T? data, string? error, int statusCode)
    {
        Success = success;
        Data = data;
        Error = error;
        StatusCode = statusCode;
    }

    public bool Success { get; }
    public T? Data { get; }
    public string? Error { get; }
    public int StatusCode { get; }

    public static Result<T> Ok(T data) => new(true, data, null, 200);
    public static Result<T> Created(T data) => new(true, data, null, 201);
    public static Result<T> NotFound(string? message = null) => new(false, default, message ?? "资源不存在", 404);
    public static Result<T> BadRequest(string error) => new(false, default, error, 400);
    public static Result<T> Conflict(string error) => new(false, default, error, 409);
    public static Result<T> Forbidden(string? message = null) => new(false, default, message ?? "无权限", 403);
}

/// <summary>无返回值的应用层结果。</summary>
public sealed class Result
{
    private Result(bool success, string? error, int statusCode)
    {
        Success = success;
        Error = error;
        StatusCode = statusCode;
    }

    public bool Success { get; }
    public string? Error { get; }
    public int StatusCode { get; }

    public static Result Ok() => new(true, null, 200);
    public static Result NoContent() => new(true, null, 204);
    public static Result NotFound(string? message = null) => new(false, message ?? "资源不存在", 404);
    public static Result BadRequest(string error) => new(false, error, 400);
    public static Result Conflict(string error) => new(false, error, 409);
}
