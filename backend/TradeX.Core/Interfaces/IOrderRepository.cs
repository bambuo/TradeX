using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Order?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken ct = default);
    Task<Order?> GetByClientOrderIdAsync(Guid clientOrderId, CancellationToken ct = default);
    Task<List<Order>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default);
    Task<List<Order>> GetPendingByExchangeAsync(Guid exchangeId, CancellationToken ct = default);
    Task<List<Order>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default);
    Task<List<Order>> GetByPositionIdAsync(Guid positionId, CancellationToken ct = default);
    /// <summary>某策略某交易对是否存在在途买单（Pending / PartiallyFilled），用于入场幂等闸跨重启兜底。</summary>
    Task<bool> HasActiveBuyAsync(Guid strategyId, string pair, CancellationToken ct = default);
    /// <summary>某持仓是否存在在途卖单（Pending / PartiallyFilled），用于平仓幂等闸跨重启兜底。</summary>
    Task<bool> HasActiveSellAsync(Guid positionId, CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    Task UpdateAsync(Order order, CancellationToken ct = default);
}
