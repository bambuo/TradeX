using System.Net;
using TradeX.Core.ErrorCodes;

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

            var logger = context.RequestServices.GetService<ILogger<ExceptionHandlingMiddleware>>();
            logger?.LogError(ex, "未处理的异常, TraceId={TraceId}, Path={Path}, Method={Method}",
                traceId, context.Request.Path, context.Request.Method);

            await context.Response.WriteAsync(ApiResponse.Error(BusinessErrorCode.InternalError, "服务器内部错误").ToJson());
        }
    }
}
