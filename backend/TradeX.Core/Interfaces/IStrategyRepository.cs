using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IStrategyRepository
{
    Task<Strategy?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Strategy>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(Strategy strategy, CancellationToken ct = default);
    Task UpdateAsync(Strategy strategy, CancellationToken ct = default);
    Task DeleteAsync(Strategy strategy, CancellationToken ct = default);
}
