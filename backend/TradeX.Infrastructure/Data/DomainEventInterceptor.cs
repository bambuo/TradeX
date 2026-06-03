using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TradeX.Core.Abstractions;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data;

/// <summary>
/// 保存前自动读取 <see cref="AggregateRoot.DomainEvents"/> 并写入 outbox_events 表。
/// 使领域事件从收集到发布的管线自动连通，领域方法只需调用 <see cref="AggregateRoot.AddDomainEvent"/>。
/// </summary>
public sealed class DomainEventInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Dispatch(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Dispatch(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Dispatch(DbContext? context)
    {
        if (context is null) return;

        var entries = context.ChangeTracker.Entries<AggregateRoot>().ToList();
        foreach (var entry in entries)
        {
            var aggregate = entry.Entity;
            var events = aggregate.DomainEvents;
            if (events.Count == 0) continue;

            foreach (var domainEvent in events)
            {
                context.Set<OutboxEvent>().Add(new OutboxEvent
                {
                    Type = domainEvent.GetType().Name,
                    PayloadJson = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), JsonOptions),
                    Status = OutboxStatus.Pending,
                    TraderId = TryGetTraderId(domainEvent)
                });
            }

            // 事件写入 outbox 后清空聚合根的事件集合，防止重复发布
            aggregate.ClearDomainEvents();
        }
    }

    /// <summary>尝试从领域事件中提取 TraderId（部分事件包含此属性）。</summary>
    private static Guid? TryGetTraderId(object domainEvent)
    {
        // 通过反射尝试读取 TraderId 属性（多数领域事件包含此字段）
        var prop = domainEvent.GetType().GetProperty("TraderId");
        return prop?.GetValue(domainEvent) as Guid?;
    }
}
