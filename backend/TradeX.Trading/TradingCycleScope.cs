using Microsoft.Extensions.DependencyInjection;
using TradeX.Core.Interfaces;
using TradeX.Indicators;

namespace TradeX.Trading;

internal sealed class TradingCycleScope(IServiceScopeFactory scopeFactory) : IDisposable
{
    private readonly IServiceScope _scope = scopeFactory.CreateScope();

    public IStrategyRepository StrategyRepo => _scope.ServiceProvider.GetRequiredService<IStrategyRepository>();
    public IPositionRepository PositionRepo => _scope.ServiceProvider.GetRequiredService<IPositionRepository>();
    public IIndicatorService IndicatorService => _scope.ServiceProvider.GetRequiredService<IIndicatorService>();
    public IConditionEvaluator ConditionEvaluator => _scope.ServiceProvider.GetRequiredService<IConditionEvaluator>();
    public IPortfolioRiskManager RiskManager => _scope.ServiceProvider.GetRequiredService<IPortfolioRiskManager>();
    public ITradeExecutor TradeExecutor => _scope.ServiceProvider.GetRequiredService<ITradeExecutor>();

    public void Dispose() => _scope.Dispose();
}
