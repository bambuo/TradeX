using TradeX.Core.Models;

namespace TradeX.Core.Interfaces;

public interface IExchangeOrderHistoryRepository
{
    Task<(List<ExchangeOrderHistory> Items, int Total)> GetPagedAsync(
        Guid exchangeId, int page = 1, int pageSize = 20,
        string? pair = null, string? side = null, string? orderType = null, string? orderStatus = null,
        CancellationToken ct = default);

    Task UpsertManyAsync(IEnumerable<ExchangeOrderHistory> orders, CancellationToken ct = default);
}
