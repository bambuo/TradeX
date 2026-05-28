namespace TradeX.Trading.Engine;

/// <summary>
/// FR-03.11~14: 波动率网格策略的核心算法.
/// 围绕持仓均价进行分批加仓 / 减仓:
///   * 价格相对 AvgPrice 跌幅 ≥ RebalancePercent 且未触上限 → 加仓 BasePositionSize
///   * 价格相对 AvgPrice 涨幅 ≥ RebalancePercent 且有仓位 → 减仓 BasePositionSize (锁定部分利润)
///   * 加仓次数受 MaxPyramidingLevels 限制
///   * 总仓位金额受 MaxPositionSize 限制
///   * NoStopLoss = true 时不触发个体止损; 账户级风控由上层 RiskHandler 负责
/// 与现有 Strategy + ConditionTreeEvaluator 解耦, 由 TradingEngine 在 Volatility Grid 模式下显式调用.
/// </summary>
public sealed class VolatilityGridExecutor(VolatilityGridExecutionRule rule)
{
    public VolatilityGridDecision Decide(VolatilityGridState state, decimal currentPrice)
    {
        if (currentPrice <= 0) return VolatilityGridDecision.Hold("非法价格");

        // 无持仓: 直接开第 1 仓
        if (state.QuantityHeld <= 0)
        {
            var qty = rule.BasePositionSize / currentPrice;
            return VolatilityGridDecision.Buy(qty, $"首次入场 (level=1)", newLevel: 1);
        }

        var avg = state.AverageEntryPrice;
        if (avg <= 0) return VolatilityGridDecision.Hold("均价无效");

        var deviationPct = (currentPrice - avg) / avg * 100m;

        // 下跌触发加仓
        if (deviationPct <= -rule.RebalancePercent)
        {
            if (state.PyramidingLevel >= rule.MaxPyramidingLevels)
                return VolatilityGridDecision.Hold($"已达加仓上限 ({rule.MaxPyramidingLevels})");

            var projectedNotional = state.QuantityHeld * avg + rule.BasePositionSize;
            if (projectedNotional > rule.MaxPositionSize)
                return VolatilityGridDecision.Hold($"加仓后名义价值 {projectedNotional:F2} 超过上限 {rule.MaxPositionSize:F2}");

            var qty = rule.BasePositionSize / currentPrice;
            return VolatilityGridDecision.Buy(qty, $"下跌 {deviationPct:F2}% 触发加仓", newLevel: state.PyramidingLevel + 1);
        }

        // 上涨触发减仓
        if (deviationPct >= rule.RebalancePercent)
        {
            var sellQty = Math.Min(state.QuantityHeld, rule.BasePositionSize / currentPrice);
            return VolatilityGridDecision.Sell(sellQty, $"上涨 {deviationPct:F2}% 触发减仓", newLevel: Math.Max(0, state.PyramidingLevel - 1));
        }

        return VolatilityGridDecision.Hold($"偏离 {deviationPct:F2}% 未达 {rule.RebalancePercent}%");
    }
}

public sealed record VolatilityGridState(decimal AverageEntryPrice, decimal QuantityHeld, int PyramidingLevel)
{
    public static VolatilityGridState Empty { get; } = new(0m, 0m, 0);

    /// <summary>合并一次买入后的新状态 (按加权平均更新).</summary>
    public VolatilityGridState ApplyBuy(decimal price, decimal qty, int newLevel)
    {
        if (qty <= 0) return this;
        var newQty = QuantityHeld + qty;
        var newAvg = newQty > 0 ? (AverageEntryPrice * QuantityHeld + price * qty) / newQty : 0m;
        return this with { AverageEntryPrice = newAvg, QuantityHeld = newQty, PyramidingLevel = newLevel };
    }

    public VolatilityGridState ApplySell(decimal qty, int newLevel)
    {
        if (qty <= 0) return this;
        var newQty = Math.Max(0m, QuantityHeld - qty);
        var newAvg = newQty > 0 ? AverageEntryPrice : 0m;
        return this with { AverageEntryPrice = newAvg, QuantityHeld = newQty, PyramidingLevel = newLevel };
    }
}

public enum VolatilityGridAction { Hold, Buy, Sell }

public sealed record VolatilityGridDecision(VolatilityGridAction Action, decimal Quantity, string Reason, int NewLevel)
{
    public static VolatilityGridDecision Hold(string reason) => new(VolatilityGridAction.Hold, 0m, reason, 0);
    public static VolatilityGridDecision Buy(decimal qty, string reason, int newLevel) => new(VolatilityGridAction.Buy, qty, reason, newLevel);
    public static VolatilityGridDecision Sell(decimal qty, string reason, int newLevel) => new(VolatilityGridAction.Sell, qty, reason, newLevel);
}
