namespace TradeX.Api.Middleware;

/// <summary>
/// 给所有响应添加基础安全头。生产环境配合 Caddy/Nginx TLS 终止使用。
/// 注意：CSP 偏严格；如果前端用了 inline script/style，需在前端构建产物里改用 hash/nonce。
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext ctx)
    {
        var h = ctx.Response.Headers;
        // 禁止页面被 iframe 嵌入（点击劫持防护）
        h["X-Frame-Options"] = "DENY";
        // 禁止 MIME 类型嗅探
        h["X-Content-Type-Options"] = "nosniff";
        // Referrer 仅在同源时携带
        h["Referrer-Policy"] = "strict-origin-when-cross-origin";
        // 限制浏览器 API 访问
        h["Permissions-Policy"] = "geolocation=(), microphone=(), camera=(), payment=()";
        // 强制 HTTPS（仅在 TLS 终止后生效；前面无 https 时浏览器忽略）
        h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        // 内容安全策略（SPA + 自身 API；按需扩 connect-src 给 SignalR / 第三方）
        h["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline'; " +   // SPA 通常需要 inline 启动
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: https:; " +
            "connect-src 'self' wss: https:; " +
            "frame-ancestors 'none';";
        return next(ctx);
    }
}
