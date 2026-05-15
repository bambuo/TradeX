using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Infrastructure.Data.Repositories;

public class OrderRepository(TradeXDbContext context) : IOrderRepository
{
    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Orders.FirstOrDefaultAsync(o => o.Id == id, ct);

    public async Task<Order?> GetByExchangeOrderIdAsync(string exchangeOrderId, CancellationToken ct = default)
        => await context.Orders.FirstOrDefaultAsync(o => o.ExchangeOrderId == exchangeOrderId, ct);

    public async Task<Order?> GetByClientOrderIdAsync(Guid clientOrderId, CancellationToken ct = default)
        => await context.Orders.FirstOrDefaultAsync(o => o.ClientOrderId == clientOrderId, ct);

    public async Task<List<Order>> GetByTraderIdAsync(Guid traderId, CancellationToken ct = default)
        => await context.Orders.Where(o => o.TraderId == traderId).OrderByDescending(o => o.PlacedAtUtc).ToListAsync(ct);

    public async Task<List<Order>> GetPendingByExchangeAsync(Guid exchangeId, CancellationToken ct = default)
        => await context.Orders
            .Where(o => o.ExchangeId == exchangeId && o.Status == Core.Enums.OrderStatus.Pending)
            .ToListAsync(ct);

    public async Task<List<Order>> GetByStrategyIdAsync(Guid strategyId, CancellationToken ct = default)
        => await context.Orders.Where(o => o.StrategyId == strategyId).OrderByDescending(o => o.PlacedAtUtc).ToListAsync(ct);

    public async Task<List<Order>> GetByPositionIdAsync(Guid positionId, CancellationToken ct = default)
        => await context.Orders.Where(o => o.PositionId == positionId).OrderByDescending(o => o.PlacedAtUtc).ToListAsync(ct);

    public async Task AddAsync(Order order, CancellationToken ct = default)
    {
        await context.Orders.AddAsync(order, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Order order, CancellationToken ct = default)
    {
        context.Orders.Update(order);
        await context.SaveChangesAsync(ct);
    }
}
