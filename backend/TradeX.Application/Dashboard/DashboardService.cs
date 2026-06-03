using TradeX.Application.Common;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Application.Dashboard;

/// <summary>
/// 仪表盘统计服务。通过现有仓储接口聚合数据，不依赖 Infrastructure。
/// </summary>
public sealed class DashboardService(
    IStrategyBindingRepository bindingRepo,
    IPositionRepository positionRepo,
    IExchangeRepository exchangeRepo) : IDashboardService
{
    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        // 策略统计
        var allBindings = await bindingRepo.GetActiveByExchangeAndPairAsync(Guid.Empty, string.Empty, ct);
        var activeStrategies = await bindingRepo.GetAllActiveAsync(ct);
        var strategyCount = activeStrategies.Count;

        // 持仓统计
        var openPositions = await positionRepo.GetAllOpenAsync(ct);
        var openPositionCount = openPositions.Count;
        var totalBalance = openPositions.Sum(p => p.CurrentPrice * p.Quantity);
        var totalPnl = openPositions.Sum(p => p.UnrealizedPnl + p.RealizedPnl);

        // 交易所状态
        var exchanges = await exchangeRepo.GetAllEnabledAsync(ct);
        var exchangeStatus = exchanges.ToDictionary(
            e => e.Type.ToString().ToLowerInvariant(),
            _ => (string?)"Connected");

        return new DashboardSummary(
            0, strategyCount, activeStrategies.Count,
            openPositionCount, 0, totalBalance, totalPnl,
            0m, exchangeStatus, []);
    }
}
