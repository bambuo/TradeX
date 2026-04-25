using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IStrategyDeploymentRepository
{
    Task<StrategyDeployment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<StrategyDeployment>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default);
    Task<List<StrategyDeployment>> GetAllByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<List<StrategyDeployment>> GetAllActiveAsync(CancellationToken ct = default);
    Task<List<StrategyDeployment>> GetActiveByExchangeAndSymbolAsync(Guid exchangeId, string symbolId, CancellationToken ct = default);
    Task<bool> ExistsActiveAsync(Guid traderId, Guid exchangeId, string symbolId, Guid? excludeId = null, CancellationToken ct = default);
    Task AddAsync(StrategyDeployment deployment, CancellationToken ct = default);
    Task UpdateAsync(StrategyDeployment deployment, CancellationToken ct = default);
    Task DeleteAsync(StrategyDeployment deployment, CancellationToken ct = default);
}
