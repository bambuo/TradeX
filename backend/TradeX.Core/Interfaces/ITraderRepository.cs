using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface ITraderRepository
{
    Task<Trader?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Trader>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsNameUniqueAsync(Guid userId, string name, CancellationToken ct = default);
    Task AddAsync(Trader trader, CancellationToken ct = default);
    Task UpdateAsync(Trader trader, CancellationToken ct = default);
    Task DeleteAsync(Trader trader, CancellationToken ct = default);
}
