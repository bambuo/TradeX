using System.Text.Json;

namespace TradeX.Core.ErrorCodes;

/// <summary>
/// 统一 API 响应格式。
/// <code>
/// { "code": 0, "message": "", "data": T }
/// </code>
/// code: 0 表示成功，非 0 对应 <see cref="BusinessErrorCode"/> 枚举值。
/// </summary>
public sealed class ApiResponse
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public int Code { get; }
    public string Message { get; }
    public object? Data { get; }

    private ApiResponse(int code, string message, object? data)
    {
        Code = code;
        Message = message;
        Data = data;
    }

    public static ApiResponse Ok<T>(T data) => new(0, "", data);
    public static ApiResponse Ok() => new(0, "", null);
    public static ApiResponse Error(int code, string message) => new(code, message, null);
    public static ApiResponse Error(BusinessErrorCode code, string message) => new((int)code, message, null);

    /// <summary>序列化为 JSON 字符串（用于 Middleware 直接写入响应体）。</summary>
    public string ToJson() =>
        JsonSerializer.Serialize(this, JsonOptions);
}
