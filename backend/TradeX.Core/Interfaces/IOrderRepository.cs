using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Order?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken ct = default);
    Task<List<Order>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default);
    Task<List<Order>> GetPendingByExchangeAsync(Guid exchangeId, CancellationToken ct = default);
    Task<List<Order>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default);
    Task<List<Order>> GetByPositionIdAsync(Guid positionId, CancellationToken ct = default);
    Task AddAsync(Order order, CancellationToken ct = default);
    Task UpdateAsync(Order order, CancellationToken ct = default);
}
