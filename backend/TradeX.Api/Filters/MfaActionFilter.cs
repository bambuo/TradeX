using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using StackExchange.Redis;
using TradeX.Api.Services;
using TradeX.Core.ErrorCodes;
using TradeX.Core.Interfaces;
using TradeX.Trading.Observability;

namespace TradeX.Api.Filters;

public sealed class MfaActionFilter(
    IUserRepository userRepository,
    MfaService mfaService,
    IEncryptionService encryptionService,
    TradeXMetrics metrics,
    IServiceProvider sp,
    ILogger<MfaActionFilter> logger) : IAsyncActionFilter
{
    private const string HeaderName = "X-MFA-Code";
    private static readonly TimeSpan ReplayWindowTtl = TimeSpan.FromSeconds(120);

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!HasRequireMfa(context))
        {
            await next();
            return;
        }

        var http = context.HttpContext;
        var userIdClaim = http.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            context.Result = new UnauthorizedObjectResult(new ErrorResponse(BusinessErrorCode.Unauthenticated, "未认证", http.TraceIdentifier));
            return;
        }

        if (!http.Request.Headers.TryGetValue(HeaderName, out var headerValues)
            || string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            metrics.MfaAttempts.Add(1, new KeyValuePair<string, object?>("result", "missing"));
            context.Result = new ObjectResult(new ErrorResponse(BusinessErrorCode.AuthMfaRequired, $"敏感操作需在请求头 {HeaderName} 中携带 TOTP 验证码", http.TraceIdentifier))
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        var code = headerValues.ToString().Trim();
        var user = await userRepository.GetByIdAsync(userId, http.RequestAborted);
        if (user is null || !user.IsMfaEnabled || string.IsNullOrWhiteSpace(user.MfaSecretEncrypted))
        {
            metrics.MfaAttempts.Add(1, new KeyValuePair<string, object?>("result", "not_configured"));
            context.Result = new ObjectResult(new ErrorResponse(BusinessErrorCode.AuthMfaNotConfigured, "当前账户未启用 MFA，无法执行敏感操作。请先在账户设置中启用 MFA。", http.TraceIdentifier))
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        string secret;
        try
        {
            secret = encryptionService.Decrypt(user.MfaSecretEncrypted);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "MFA secret 解密失败, UserId={UserId}", userId);
            context.Result = new ObjectResult(new ErrorResponse(BusinessErrorCode.AuthMfaSecretInvalid, "MFA 密钥无效", http.TraceIdentifier))
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            return;
        }

        if (!mfaService.ValidateTotp(secret, code))
        {
            metrics.MfaAttempts.Add(1, new KeyValuePair<string, object?>("result", "invalid"));
            logger.LogWarning("MFA 校验失败, UserId={UserId}, Endpoint={Endpoint}", userId, http.Request.Path);
            context.Result = new ObjectResult(new ErrorResponse(BusinessErrorCode.AuthMfaInvalidCode, "TOTP 验证码错误或已过期", http.TraceIdentifier))
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        var redis = sp.GetService<IConnectionMultiplexer>();
        if (redis is not null)
        {
            var key = $"tradex:mfa:used:{userId:N}:{code}";
            var acquired = await redis.GetDatabase().StringSetAsync(key, "1", ReplayWindowTtl, when: When.NotExists);
            if (!acquired)
            {
                metrics.MfaAttempts.Add(1, new KeyValuePair<string, object?>("result", "replay"));
                logger.LogWarning("MFA 重放尝试被拒, UserId={UserId}, Endpoint={Endpoint}", userId, http.Request.Path);
                context.Result = new ObjectResult(new ErrorResponse(BusinessErrorCode.AuthMfaReplayDetected, "TOTP 验证码已被使用过，请等待下一个 30 秒周期", http.TraceIdentifier))
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
                return;
            }
        }
        else
        {
            logger.LogDebug("Redis 未配置，MFA 重放保护降级 — 不推荐生产使用");
        }

        metrics.MfaAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));
        logger.LogInformation("MFA 校验通过, UserId={UserId}, Endpoint={Endpoint}", userId, http.Request.Path);

        await next();
    }

    private static bool HasRequireMfa(ActionExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
            return false;

        return descriptor.MethodInfo.GetCustomAttributes(typeof(RequireMfaAttribute), inherit: true).Length > 0
            || descriptor.ControllerTypeInfo.GetCustomAttributes(typeof(RequireMfaAttribute), inherit: true).Length > 0;
    }
}
