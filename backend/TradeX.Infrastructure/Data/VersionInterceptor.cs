using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TradeX.Core.Interfaces;

namespace TradeX.Infrastructure.Data;

/// <summary>
/// 保存前自动刷新 <see cref="IVersioned.Version"/>，配合实体配置中的 <c>IsConcurrencyToken</c>
/// 提供乐观并发：UPDATE WHERE Id=? AND Version=? — 行版本不匹配时返回 0 行影响，
/// EF 抛 DbUpdateConcurrencyException 供调用方重试/合并。
/// </summary>
public sealed class VersionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Apply(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries<IVersioned>())
        {
            if (entry.State == EntityState.Modified || entry.State == EntityState.Added)
                entry.Entity.Version = Guid.NewGuid();
        }
    }
}
