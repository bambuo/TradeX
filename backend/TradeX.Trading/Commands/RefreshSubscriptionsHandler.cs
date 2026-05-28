using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeX.Trading.Streaming;

namespace TradeX.Trading.Commands;

/// <summary>触发 KlineStreamManager 和 StrategyEvaluationConsumer 刷新订阅与策略缓存。</summary>
public sealed class RefreshSubscriptionsHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<RefreshSubscriptionsHandler> logger) : IWorkerCommandHandler
{
    public string CommandType => WorkerCommandTypes.RefreshSubscriptions;

    public async Task HandleAsync(string argsJson, CancellationToken ct)
    {
        logger.LogInformation("收到 RefreshSubscriptions 命令，刷新 K 线订阅和策略缓存");

        // StrategyEvaluationConsumer 是 Singleton，可直接从 root scope 获取
        using var scope = scopeFactory.CreateScope();
        var consumer = scope.ServiceProvider.GetRequiredService<StrategyEvaluationConsumer>();
        await consumer.RefreshAsync(ct);
    }
}
