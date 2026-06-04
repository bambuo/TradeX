using TradeX.Core.Models;

namespace TradeX.Trading.Execution;

/// <summary>
/// 「成交 → 持仓」投影器：当订单转为 Filled 时，把成交投影为持仓变更
/// （买入 → 开仓，卖出 → 平仓）。自带幂等，可被实盘同步路径与对账器恢复路径重复调用。
/// </summary>
public interface IFillProjector
{
    /// <summary>
    /// 在订单刚转为 <see cref="Core.Enums.OrderStatus.Filled"/> 且已取得成交均价后调用。
    /// </summary>
    /// <param name="order">已成交的订单（FilledQuantity / Side / StrategyId / PositionId 已就绪）。</param>
    /// <param name="avgFillPrice">交易所返回的成交均价；&lt;=0 时内部按金额/数量或委托价兜底。</param>
    Task ProjectFilledAsync(Order order, decimal avgFillPrice, CancellationToken ct = default);
}
