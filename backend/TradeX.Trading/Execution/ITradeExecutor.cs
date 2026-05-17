using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Trading.Execution;

public interface ITradeExecutor
{
    Task<OrderResult> ExecuteMarketOrderAsync(Order order, CancellationToken ct = default);
    Task<OrderResult> ExecuteLimitOrderAsync(Order order, CancellationToken ct = default);
    Task<OrderResult> ExecuteStopLimitOrderAsync(Order order, decimal stopPrice, CancellationToken ct = default);
}
