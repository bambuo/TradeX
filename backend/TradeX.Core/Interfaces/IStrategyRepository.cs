using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IStrategyRepository
{
    Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Strategy>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default);
    Task<List<Strategy>> GetAllActiveAsync(CancellationToken ct = default);
    Task<List<Strategy>> GetActiveByExchangeAndSymbolAsync(Guid exchangeId, string symbolId, CancellationToken ct = default);
    Task<bool> ExistsActiveAsync(Guid traderId, Guid exchangeId, string symbolId, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(Strategy strategy, CancellationToken ct = default);
    Task UpdateAsync(Strategy strategy, CancellationToken ct = default);
    Task DeleteAsync(Strategy strategy, CancellationToken ct = default);
}
