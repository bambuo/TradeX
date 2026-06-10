using FlexLabs.EntityFrameworkCore.Upsert;
using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class ExchangeOrderHistoryRepository(TradeXDbContext db)
    : IExchangeOrderHistoryRepository
{
    public async Task<(List<ExchangeOrderHistory> Items, int Total)> GetPagedAsync(
        Guid exchangeId, int page = 1, int pageSize = 20,
        string? pair = null, string? side = null, string? orderType = null, string? orderStatus = null,
        CancellationToken ct = default)
    {
        var query = db.ExchangeOrderHistories
            .Where(h => h.ExchangeId == exchangeId);

        if (pair is not null)
            query = query.Where(h => h.Pair == pair);
        if (side is not null)
            query = query.Where(h => h.Side == side);
        if (orderType is not null)
            query = query.Where(h => h.Type == orderType);
        if (orderStatus is not null)
            query = query.Where(h => h.Status == orderStatus);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(h => h.PlacedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task UpsertManyAsync(IEnumerable<ExchangeOrderHistory> orders, CancellationToken ct = default)
    {
        var list = orders.ToList();
        if (list.Count == 0) return;

        await db.ExchangeOrderHistories
            .UpsertRange(list)
            .On(v => new { v.ExchangeId, v.ExchangeOrderId })
            .WhenMatched(v => new ExchangeOrderHistory
            {
                Status = v.Status,
                FilledQuantity = v.FilledQuantity,
                Price = v.Price,
                Quantity = v.Quantity,
                SyncedAt = v.SyncedAt
            })
            .RunAsync(ct);
    }
}
