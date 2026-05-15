namespace TradeX.Api.Filters;

/// <summary>
/// 标记需要 MFA 二次确认的接口（敏感配置变更）。
/// 客户端必须在请求头 <c>X-MFA-Code</c> 携带当前账户的 TOTP 6 位码。
/// 实际校验由全局过滤器 <see cref="MfaActionFilter"/> 完成。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RequireMfaAttribute : Attribute;
