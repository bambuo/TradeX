using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface ISystemConfigRepository
{
    Task<List<SystemConfig>> GetAllAsync(CancellationToken ct = default);
    Task<SystemConfig?> GetByKeyAsync(string key, CancellationToken ct = default);
    Task UpsertAsync(string key, string value, CancellationToken ct = default);
}
