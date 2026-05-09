using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IStrategyBindingRepository
{
    Task<StrategyBinding?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<StrategyBinding>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default);
    Task<List<StrategyBinding>> GetAllByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<List<StrategyBinding>> GetAllActiveAsync(CancellationToken ct = default);
    Task<List<StrategyBinding>> GetActiveByExchangeAndPairAsync(Guid exchangeId, string pair, CancellationToken ct = default);
    Task<List<StrategyBinding>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default);
    Task<bool> ExistsActiveAsync(Guid traderId, Guid exchangeId, string pair, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(StrategyBinding deployment, CancellationToken ct = default);
    Task UpdateAsync(StrategyBinding deployment, CancellationToken ct = default);
    Task DeleteAsync(StrategyBinding deployment, CancellationToken ct = default);
}
