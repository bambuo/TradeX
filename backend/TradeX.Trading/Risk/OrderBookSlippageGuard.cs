using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Trading.Risk;

/// <summary>
/// 基于实时订单簿深度的滑点估算. 区别于 SlippageHandler (按 tolerance 估算理论滑点),
/// 这里"走簿"模拟实际成交价, 给出 (averageFillPrice, slippagePercent).
///
/// 用法: 下市价/激进限价单前调用 EstimateAsync, 若 slippagePercent 超阈值 → TradeExecutor 拒绝下单.
/// </summary>
public sealed class OrderBookSlippageGuard
{
    public SlippageEstimate Estimate(OrderBook book, OrderSide side, decimal quantity, decimal referencePrice)
    {
        if (quantity <= 0 || referencePrice <= 0)
            return new SlippageEstimate(false, 0m, 0m, "非法数量或参考价");

        // book.Bids / Asks 是 N×2 二维数组: [i,0] = price, [i,1] = quantity, 按深度排序 (买盘从高到低, 卖盘从低到高)
        var ladder = side == OrderSide.Buy ? book.Asks : book.Bids;
        if (ladder is null || ladder.GetLength(0) == 0)
            return new SlippageEstimate(false, 0m, 0m, "订单簿为空");

        decimal remaining = quantity;
        decimal notional = 0m;
        var levels = ladder.GetLength(0);

        for (var i = 0; i < levels && remaining > 0; i++)
        {
            var price = ladder[i, 0];
            var available = ladder[i, 1];
            if (price <= 0 || available <= 0) continue;
            var take = Math.Min(remaining, available);
            notional += take * price;
            remaining -= take;
        }

        if (remaining > 0)
            return new SlippageEstimate(false, 0m, 0m, $"订单簿深度不足, 缺口 {remaining}");

        var avgPrice = notional / quantity;
        var slippagePct = (avgPrice - referencePrice) / referencePrice * 100m;
        // 买入滑点应为正 (实际价 >= 参考价), 卖出滑点取反保持"正向不利偏离"语义
        if (side == OrderSide.Sell) slippagePct = -slippagePct;

        return new SlippageEstimate(true, avgPrice, slippagePct, null);
    }
}

public sealed record SlippageEstimate(bool Sufficient, decimal AverageFillPrice, decimal SlippagePercent, string? Reason);
