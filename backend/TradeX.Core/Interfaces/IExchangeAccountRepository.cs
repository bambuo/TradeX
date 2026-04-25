using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IExchangeAccountRepository
{
    Task<ExchangeAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ExchangeAccount>> GetAllEnabledAsync(CancellationToken ct = default);
    Task<List<ExchangeAccount>> GetAllByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsNameUniqueAsync(string name, CancellationToken ct = default);
    Task AddAsync(ExchangeAccount account, CancellationToken ct = default);
    Task UpdateAsync(ExchangeAccount account, CancellationToken ct = default);
    Task DeleteAsync(ExchangeAccount account, CancellationToken ct = default);
}
