using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TradeX.Trading.Execution;

namespace TradeX.Trading.Commands;

/// <summary>立即触发一次订单对账（无 args）。</summary>
public sealed class ReconcileNowHandler(
    IServiceScopeFactory scopeFactory,
    ILogger<ReconcileNowHandler> logger) : IWorkerCommandHandler
{
    public string CommandType => WorkerCommandTypes.ReconcileNow;

    public async Task HandleAsync(string argsJson, CancellationToken ct)
    {
        logger.LogInformation("收到 ReconcileNow 命令，触发一次对账");
        using var scope = scopeFactory.CreateScope();
        var reconciler = scope.ServiceProvider.GetRequiredService<IOrderReconciler>();
        await reconciler.ReconcileAsync(ct);
    }
}
