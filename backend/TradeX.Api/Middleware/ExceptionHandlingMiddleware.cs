using System.Net;
using System.Text.Json;
using TradeX.Core.Interfaces;

namespace TradeX.Api.Middleware;

public class ExceptionHandlingMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";

            var traceId = context.TraceIdentifier;
            var response = new
            {
                code = "SYSTEM_INTERNAL_ERROR",
                message = "服务器内部错误",
                traceId
            };

            var logger = context.RequestServices.GetService<ILogger<ExceptionHandlingMiddleware>>();
            logger?.LogError(ex, "未处理的异常, TraceId={TraceId}, Path={Path}, Method={Method}",
                traceId, context.Request.Path, context.Request.Method);

            await context.Response.WriteAsync(JsonSerializer.Serialize(response));
        }
    }
}
