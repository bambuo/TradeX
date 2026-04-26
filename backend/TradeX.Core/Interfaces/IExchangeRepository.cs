using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IExchangeRepository
{
    Task<Exchange?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<Exchange>> GetAllEnabledAsync(CancellationToken ct = default);
    Task<List<Exchange>> GetAllByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<bool> IsNameUniqueAsync(string name, CancellationToken ct = default);
    Task AddAsync(Exchange exchange, CancellationToken ct = default);
    Task UpdateAsync(Exchange exchange, CancellationToken ct = default);
    Task DeleteAsync(Exchange exchange, CancellationToken ct = default);
}
