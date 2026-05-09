using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IPositionRepository
{
    Task<Position?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Position>> GetOpenByTraderIdAsync(Guid traderId, CancellationToken ct = default);
    Task<List<Position>> GetAllOpenAsync(CancellationToken ct = default);
    Task<List<Position>> GetOpenByPairAsync(Guid exchangeId, string pair, CancellationToken ct = default);
    Task<List<Position>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default);
    Task<List<Position>> GetClosedByTraderIdSinceAsync(Guid traderId, DateTime since, CancellationToken ct = default);
    Task AddAsync(Position position, CancellationToken ct = default);
    Task UpdateAsync(Position position, CancellationToken ct = default);
}
