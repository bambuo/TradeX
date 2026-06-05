using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;

namespace TradeX.Infrastructure.Data;

/// <summary>
/// 领域事件分发器。找出已注册的 <see cref="IDomainEventHandler{TEvent}"/> 并逐个调用。
/// handler 运行在 SaveChanges 成功之后（事务已提交），handler 失败不会回滚业务数据。
/// </summary>
public sealed class DomainEventDispatcher(
    IServiceProvider serviceProvider,
    ILogger<DomainEventDispatcher> logger)
{
    public async Task DispatchAsync(IReadOnlyList<IDomainEvent> events, CancellationToken ct)
    {
        foreach (var domainEvent in events)
        {
            if (ct.IsCancellationRequested) break;

            var eventType = domainEvent.GetType();
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(eventType);

            try
            {
                var handlers = serviceProvider.GetServices(handlerType);
                if (handlers is null) continue;

                var handlerList = ((IEnumerable<object>)handlers).ToList();
                if (handlerList.Count == 0)
                {
                    logger.LogDebug("领域事件 {Type} 无已注册的 Handler，跳过", eventType.Name);
                    continue;
                }

                foreach (var handler in handlerList)
                {
                    var method = handlerType.GetMethod("HandleAsync");
                    if (method is not null)
                    {
                        var task = (Task)method.Invoke(handler, [domainEvent, ct])!;
                        await task;
                    }
                }
            }
            catch (Exception ex)
            {
                // Handler 失败不抛异常——业务数据已提交，仅记录日志
                logger.LogError(ex, "领域事件 {Type} 的 Handler 执行异常", eventType.Name);
            }
        }
    }
}
