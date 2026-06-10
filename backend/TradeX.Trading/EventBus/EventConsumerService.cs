using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TradeX.Trading.EventBus;

/// <summary>
/// 事件消费者调度服务。负责：
/// <list type="bullet">
/// <item>在启动时扫描 <c>[DomainEventHandler]</c> 属性，构建事件类型 → 消费者列表的映射</item>
/// <item>在运行时分发反序列化的事件载荷到对应的消费者</item>
/// </list>
/// </summary>
internal sealed class EventConsumerService
{
    private readonly ConcurrentDictionary<Type, IEventConsumer[]> _consumerMap = new();
    private readonly object _lock = new();
    private bool _initialized;

    private readonly IServiceProvider _services;
    private readonly ILogger<EventConsumerService> _logger;

    public EventConsumerService(IServiceProvider services, ILogger<EventConsumerService> logger)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>确保消费者映射已初始化（线程安全，只执行一次）。</summary>
    public void EnsureInitialized()
    {
        if (_initialized) return;
        lock (_lock)
        {
            if (_initialized) return;
            BuildConsumerMap();
            _initialized = true;
        }
    }

    /// <summary>分发事件载荷到所有已注册的消费者。</summary>
    public async Task DispatchAsync(Type eventType, string dataJson, Guid traceId, CancellationToken ct)
    {
        if (!_consumerMap.TryGetValue(eventType, out var consumers))
        {
            _logger.LogWarning("未找到事件类型的消费者: {Type}", eventType.FullName);
            return;
        }

        foreach (var consumer in consumers)
        {
            try
            {
                await consumer.ConsumeAsync(dataJson, traceId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "事件消费失败: Type={Type} TraceId={TraceId}", eventType.FullName, traceId);
            }
        }
    }

    private void BuildConsumerMap()
    {
        using var scope = _services.CreateScope();
        var sp = scope.ServiceProvider;

        // 1. 扫描所有实现 IEventConsumer 的已注册服务 (EventConsumer<T>)
        var typedConsumers = sp.GetServices<IEventConsumer>();
        foreach (var consumer in typedConsumers)
        {
            AddConsumer(consumer.EventType, consumer);
        }

        // 2. 扫描 [DomainEventHandler] 标记的方法
        var allServices = sp.GetServices<object>();
        foreach (var service in allServices)
        {
            var type = service.GetType();
            ScanTypeForMethodHandlers(type, service);

            // 如果类本身有 [DomainEventHandler]，也扫描其基类（代理/装饰器场景）
            foreach (var iface in type.GetInterfaces())
                ScanTypeForMethodHandlers(iface, service);
        }

        _logger.LogInformation("事件消费者映射已构建: 共 {Count} 个事件类型", _consumerMap.Count);
    }

    private void ScanTypeForMethodHandlers(Type type, object instance)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        foreach (var method in methods)
        {
            var attributes = method.GetCustomAttributes<DomainEventHandlerAttribute>();
            foreach (var attr in attributes)
            {
                if (CreateMethodConsumer(method, instance, attr.EventType) is { } consumer)
                    AddConsumer(attr.EventType, consumer);
            }
        }
    }

    private static IEventConsumer? CreateMethodConsumer(MethodInfo method, object instance, Type eventType)
    {
        var parameters = method.GetParameters();
        if (parameters.Length is < 1 or > 3)
            return null;

        // 验证签名: (TPayload payload, Guid traceId, CancellationToken ct) 或子集
        if (parameters[0].ParameterType != eventType)
            return null;

        return new MethodEventConsumer(
            async (dataJson, traceId, ct) =>
            {
                var payload = JsonSerializer.Deserialize(dataJson, eventType, DomainEventBusBase.JsonOptions);
                if (payload is null)
                    return;

                List<object?> args = [payload];
                if (parameters.Length >= 2)
                    args.Add(traceId);
                if (parameters.Length >= 3)
                    args.Add(ct);

                try
                {
                    var task = (Task?)method.Invoke(instance, [.. args]);
                    if (task is not null)
                        await task;
                }
                catch (TargetInvocationException ex) when (ex.InnerException is not null)
                {
                    throw ex.InnerException;
                }
            },
            eventType);
    }

    private void AddConsumer(Type eventType, IEventConsumer consumer)
    {
        _consumerMap.AddOrUpdate(eventType,
            _ => [consumer],
            (_, existing) => [.. existing, consumer]);
    }
}
