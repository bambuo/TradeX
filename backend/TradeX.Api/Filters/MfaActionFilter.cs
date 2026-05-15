using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using TradeX.Api.Services;
using TradeX.Core.Interfaces;
using TradeX.Trading.Observability;

namespace TradeX.Api.Filters;

/// <summary>
/// 全局过滤器：对带 <see cref="RequireMfaAttribute"/> 的接口强制校验请求头 X-MFA-Code 中的 TOTP 码。
/// </summary>
public sealed class MfaActionFilter(
    IUserRepository userRepository,
    MfaService mfaService,
    IEncryptionService encryptionService,
    TradeXMetrics metrics,
    ILogger<MfaActionFilter> logger) : IAsyncActionFilter
{
    private const string HeaderName = "X-MFA-Code";

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
            context.Result = new UnauthorizedObjectResult(new { error = "unauthenticated" });
            return;
        }

        if (!http.Request.Headers.TryGetValue(HeaderName, out var headerValues)
            || string.IsNullOrWhiteSpace(headerValues.ToString()))
        {
            metrics.MfaAttempts.Add(1, new KeyValuePair<string, object?>("result", "missing"));
            context.Result = new ObjectResult(new
            {
                error = "mfa_required",
                message = $"敏感操作需在请求头 {HeaderName} 中携带 TOTP 验证码"
            })
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
            context.Result = new ObjectResult(new
            {
                error = "mfa_not_configured",
                message = "当前账户未启用 MFA，无法执行敏感操作。请先在账户设置中启用 MFA。"
            })
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
            context.Result = new ObjectResult(new { error = "mfa_secret_invalid" })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            return;
        }

        if (!mfaService.ValidateTotp(secret, code))
        {
            metrics.MfaAttempts.Add(1, new KeyValuePair<string, object?>("result", "invalid"));
            logger.LogWarning("MFA 校验失败, UserId={UserId}, Endpoint={Endpoint}",
                userId, http.Request.Path);
            context.Result = new ObjectResult(new
            {
                error = "mfa_invalid",
                message = "TOTP 验证码错误或已过期"
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        metrics.MfaAttempts.Add(1, new KeyValuePair<string, object?>("result", "success"));
        logger.LogInformation("MFA 校验通过, UserId={UserId}, Endpoint={Endpoint}",
            userId, http.Request.Path);

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
