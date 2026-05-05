using Microsoft.Extensions.DependencyInjection;
using TradeX.Core.Interfaces;
using TradeX.Indicators;

namespace TradeX.Trading;

internal sealed class BacktestCycleScope(IServiceScopeFactory scopeFactory) : IDisposable
{
    private readonly IServiceScope _scope = scopeFactory.CreateScope();

    public IBacktestTaskRepository TaskRepo => _scope.ServiceProvider.GetRequiredService<IBacktestTaskRepository>();
    public IStrategyRepository StrategyRepo => _scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
    public IExchangeRepository ExchangeRepo => _scope.ServiceProvider.GetRequiredService<IExchangeRepository>();
    public IExchangeClientFactory ClientFactory => _scope.ServiceProvider.GetRequiredService<IExchangeClientFactory>();
    public IEncryptionService EncryptionService => _scope.ServiceProvider.GetRequiredService<IEncryptionService>();
    public IIndicatorService IndicatorService => _scope.ServiceProvider.GetRequiredService<IIndicatorService>();
    public IConditionEvaluator ConditionEvaluator => _scope.ServiceProvider.GetRequiredService<IConditionEvaluator>();
    public IStrategyDeploymentRepository StrategyDeploymentRepo => _scope.ServiceProvider.GetRequiredService<IStrategyDeploymentRepository>();

    public void Dispose() => _scope.Dispose();
}
