using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace TradeX.Trading.EventBus;

/// <summary>
/// 领域事件总线的 DI 注册扩展。
/// </summary>
public static class DependencyInjectionExtensions
{
    /// <summary>
    /// 注册生产者端 <see cref="IDomainEventBus"/>。
    /// Redis 可用时使用 <see cref="RedisDomainEventBus"/>，否则降级为 <see cref="NullDomainEventBus"/>。
    /// </summary>
    public static IServiceCollection AddDomainEventBus(this IServiceCollection services, bool redisAvailable)
    {
        if (redisAvailable)
            services.TryAddSingleton<IDomainEventBus, RedisDomainEventBus>();
        else
            services.TryAddSingleton<IDomainEventBus, NullDomainEventBus>();
        return services;
    }

    /// <summary>
    /// 注册消费者端：<see cref="EventConsumerService"/> 及 <see cref="RedisEventConsumerService"/> 后台服务。
    /// 仅在 Worker 进程（需要消费 Redis Stream）时调用。
    /// 要求 <c>IConnectionMultiplexer</c> 已注册。
    /// </summary>
    public static IServiceCollection AddDomainEventConsumer(this IServiceCollection services)
    {
        services.TryAddSingleton<EventConsumerService>();
        services.AddHostedService<RedisEventConsumerService>();
        return services;
    }
}
