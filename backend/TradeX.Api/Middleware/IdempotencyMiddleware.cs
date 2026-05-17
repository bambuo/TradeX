using System.Text;
using StackExchange.Redis;

namespace TradeX.Api.Middleware;

/// <summary>
/// 按 <c>Idempotency-Key</c> 请求头去重写操作，TTL 24h。
/// 仅对 POST/PUT/DELETE/PATCH 生效；GET/HEAD 天然幂等。
/// 行为：
/// <list type="bullet">
///   <item>首次请求 → Redis SETNX 占座（含 userId 防跨账号串）→ 业务处理 → 缓存响应体 24h</item>
///   <item>后续相同 key 命中 → 直接返回首次响应，不再触达业务逻辑</item>
/// </list>
/// Redis 未配置时降级：不去重（仅 log）。
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next, ILogger<IdempotencyMiddleware> logger)
{
    private const string HeaderName = "Idempotency-Key";
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);
    private static readonly HashSet<string> MutatingMethods = new(StringComparer.OrdinalIgnoreCase)
        { "POST", "PUT", "DELETE", "PATCH" };

    public async Task InvokeAsync(HttpContext ctx)
    {
        // 仅 mutating 方法 + 带 Idempotency-Key 头才介入
        if (!MutatingMethods.Contains(ctx.Request.Method)
            || !ctx.Request.Headers.TryGetValue(HeaderName, out var keyHeader)
            || string.IsNullOrWhiteSpace(keyHeader))
        {
            await next(ctx);
            return;
        }

        var redis = ctx.RequestServices.GetService<IConnectionMultiplexer>();
        if (redis is null)
        {
            logger.LogDebug("Redis 未配置，Idempotency 中间件降级 — 不去重");
            await next(ctx);
            return;
        }

        var userId = ctx.User?.Identity?.IsAuthenticated == true
            ? (ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anon")
            : "anon";
        var cacheKey = $"tradex:idem:{userId}:{keyHeader}";
        var db = redis.GetDatabase();

        // 占座：成功 → 首次；失败 → 已有缓存
        var acquired = await db.StringSetAsync(cacheKey + ":lock", "1", Ttl, when: When.NotExists);
        if (!acquired)
        {
            // 已有响应缓存？
            var cached = await db.StringGetAsync(cacheKey + ":resp");
            if (cached.HasValue)
            {
                logger.LogInformation("Idempotency 命中: Key={Key}, User={User}", keyHeader, userId);
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "application/json";
                ctx.Response.Headers.Append("X-Idempotency-Replayed", "true");
                await ctx.Response.WriteAsync((string)cached!);
                return;
            }
            // 占座存在但响应尚未写入（首次请求处理中）→ 拒绝并发同 key
            ctx.Response.StatusCode = StatusCodes.Status409Conflict;
            await ctx.Response.WriteAsync($"{{\"error\":\"idempotency_in_flight\",\"key\":\"{keyHeader}\"}}");
            return;
        }

        // 首次请求 — 捕获响应体后写入缓存
        var originalBody = ctx.Response.Body;
        using var capture = new MemoryStream();
        ctx.Response.Body = capture;
        try
        {
            await next(ctx);
            capture.Position = 0;
            await capture.CopyToAsync(originalBody);

            // 仅缓存 2xx 响应
            if (ctx.Response.StatusCode >= 200 && ctx.Response.StatusCode < 300)
            {
                var body = Encoding.UTF8.GetString(capture.ToArray());
                await db.StringSetAsync(cacheKey + ":resp", body, Ttl);
            }
            else
            {
                // 失败响应 → 立刻删占座，允许重试
                await db.KeyDeleteAsync(cacheKey + ":lock");
            }
        }
        finally
        {
            ctx.Response.Body = originalBody;
        }
    }
}
