using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

/// <summary>
/// SystemConfig 仓储 + 进程内缓存（TTL 60s）。
/// 配置查询是热点路径（每次评估周期都查 risk.volatility_grid_dedup_seconds 等），缓存命中可省 DB 往返。
/// Upsert 后主动失效该 key 的缓存。
/// </summary>
public class SystemConfigRepository(TradeXDbContext context) : ISystemConfigRepository
{
    // 简易进程内缓存。多实例下 invalidation 不传播，但 60s TTL 内变更可以接受短延迟生效。
    private static readonly ConcurrentDictionary<string, (string? Value, DateTime ExpiresAtUtc)> Cache = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);

    public async Task<List<SystemConfig>> GetAllAsync(CancellationToken ct = default)
        => await context.SystemConfigs.OrderBy(x => x.Key).ToListAsync(ct);

    public async Task<SystemConfig?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        if (Cache.TryGetValue(key, out var cached) && cached.ExpiresAtUtc > now)
        {
            return cached.Value is null
                ? null
                : new SystemConfig { Key = key, Value = cached.Value };
        }

        var row = await context.SystemConfigs.FirstOrDefaultAsync(x => x.Key == key, ct);
        Cache[key] = (row?.Value, now + CacheTtl);
        return row;
    }

    public async Task UpsertAsync(string key, string value, CancellationToken ct = default)
    {
        // 显式 Update 模式适配 NoTracking 默认；同时主动失效缓存
        var existing = await context.SystemConfigs.AsTracking().FirstOrDefaultAsync(x => x.Key == key, ct);
        if (existing is not null)
        {
            existing.Value = value;
            context.SystemConfigs.Update(existing);
        }
        else
        {
            context.SystemConfigs.Add(new SystemConfig { Key = key, Value = value });
        }
        await context.SaveChangesAsync(ct);
        Cache.TryRemove(key, out _);
    }
}
