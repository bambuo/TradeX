using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;

namespace TradeX.Infrastructure.Data;

/// <summary>
/// SaveChanges 拦截器：在保存前采集聚合根中的领域事件并清空集合，
/// 在保存成功后通过 <see cref="DomainEventDispatcher"/> 分发到已注册的 handler。
///
/// 为什么不在 SavingChanges 时写 outbox？
/// 领域事件是 in-process side effect 的触发器（通知 UI、审计日志），
/// 不属于跨进程消息（后者由 <c>RedisDomainEventBus</c> 处理）。
/// handler 通过 <c>IDomainEventBus</c> 将需要推送给前端的事件桥接到交易事件管道。
/// </summary>
public sealed class DomainEventInterceptor(
    IServiceScopeFactory scopeFactory) : SaveChangesInterceptor
{
    private static readonly AsyncLocal<List<IDomainEvent>> _pendingEvents = new();

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        CollectEvents(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CollectEvents(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = default)
    {
        var events = _pendingEvents.Value;
        _pendingEvents.Value = null;

        if (events is { Count: > 0 })
        {
            await DispatchAsync(events, cancellationToken);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>采集所有跟踪的聚合根中的领域事件并清空集合。</summary>
    private static void CollectEvents(DbContext? context)
    {
        if (context is null) return;

        var entries = context.ChangeTracker.Entries<AggregateRoot>().ToList();
        if (entries.Count == 0) return;

        var allEvents = new List<IDomainEvent>();
        foreach (var entry in entries)
        {
            var aggregate = entry.Entity;
            var events = aggregate.DomainEvents;
            if (events.Count == 0) continue;

            allEvents.AddRange(events);
            aggregate.ClearDomainEvents();
        }

        if (allEvents.Count > 0)
            _pendingEvents.Value = allEvents;
    }

    /// <summary>在单独的作用域中分发事件，handler 失败不影响已提交的业务数据。</summary>
    private async Task DispatchAsync(List<IDomainEvent> events, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<DomainEventDispatcher>();
        await dispatcher.DispatchAsync(events, ct);
    }
}
