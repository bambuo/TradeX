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

        foreach (var order in list)
        {
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "ExchangeOrderHistories" (
                    "Id", "ExchangeId", "Pair", "Side", "Type", "Status",
                    "Price", "Quantity", "FilledQuantity", "ExchangeOrderId",
                    "PlacedAt", "SyncedAt")
                VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10}, {11})
                ON CONFLICT ("ExchangeId", "ExchangeOrderId")
                DO UPDATE SET
                    "Status" = EXCLUDED."Status",
                    "FilledQuantity" = EXCLUDED."FilledQuantity",
                    "Price" = EXCLUDED."Price",
                    "Quantity" = EXCLUDED."Quantity",
                    "SyncedAt" = EXCLUDED."SyncedAt";
                """,
                order.Id, order.ExchangeId, order.Pair, order.Side, order.Type,
                order.Status, order.Price, order.Quantity, order.FilledQuantity,
                order.ExchangeOrderId, order.PlacedAt, order.SyncedAt);
        }
    }
}
