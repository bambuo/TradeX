using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IPositionRepository
{
    Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Position>> GetOpenByTraderIdAsync(Guid traderId, CancellationToken ct = default);
    Task<List<Position>> GetAllOpenAsync(CancellationToken ct = default);
    Task<List<Position>> GetOpenByPairAsync(Guid exchangeId, string pair, CancellationToken ct = default);
    Task<List<Position>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default);
    /// <summary>凭开仓订单 Id 反查持仓，用于"成交→持仓"投影的幂等判重。</summary>
    Task<Position?> GetByOpeningOrderIdAsync(Guid openingOrderId, CancellationToken ct = default);
    /// <summary>取某策略某交易对下的 Open 持仓，按开仓时间升序（供卖单 FIFO 平仓）。</summary>
    Task<List<Position>> GetOpenByStrategyAndPairAsync(Guid strategyId, string pair, CancellationToken ct = default);
    Task<List<Position>> GetClosedByTraderIdSinceAsync(Guid traderId, DateTime since, CancellationToken ct = default);
    Task AddAsync(Position position, CancellationToken ct = default);
    Task UpdateAsync(Position position, CancellationToken ct = default);
}
