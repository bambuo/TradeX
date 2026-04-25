using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeX.Core.Enums;
using TradeX.Infrastructure.Data;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController(TradeXDbContext dbContext) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var activeStrategyCount = await dbContext.StrategyDeployments.CountAsync(s => s.Status == StrategyStatus.Active, ct);
        var openPositionCount = await dbContext.Positions.CountAsync(p => p.Status == PositionStatus.Open, ct);
        var todayOrderCount = await dbContext.Orders.CountAsync(
            o => o.PlacedAtUtc >= DateTime.UtcNow.Date, ct);

        var positions = await dbContext.Positions
            .Where(p => p.Status == PositionStatus.Open)
            .ToListAsync(ct);

        var totalPnl = positions.Sum(p => p.UnrealizedPnl + p.RealizedPnl);
        var totalBalance = positions.Sum(p => p.CurrentPrice * p.Quantity);
        var winRate = 0m;

        var filledOrders = await dbContext.Orders
            .Where(o => o.Status == OrderStatus.Filled && o.QuoteQuantity > 0)
            .ToListAsync(ct);
        if (filledOrders.Count > 0)
        {
            var wins = filledOrders.Count(o => o.Side == OrderSide.Sell && (o.FilledQuantity * (o.Price ?? 0)) > o.QuoteQuantity);
            winRate = Math.Round((decimal)wins / filledOrders.Count * 100, 1);
        }

        var exchangeTypes = await dbContext.ExchangeAccounts
            .Where(a => a.Status == TradeX.Core.Models.ExchangeAccountStatus.Enabled)
            .Select(a => a.Type)
            .Distinct()
            .ToListAsync(ct);

        var exchangeStatus = exchangeTypes.ToDictionary(
            et => et.ToString().ToLower(),
            et => (string?)"Connected");

        var recentTrades = await dbContext.Orders
            .Where(o => o.Status == OrderStatus.Filled)
            .OrderByDescending(o => o.PlacedAtUtc)
            .Take(10)
            .Select(o => new
            {
                orderId = o.Id,
                symbolId = o.SymbolId,
                side = o.Side.ToString(),
                quantity = o.Quantity,
                price = o.Price ?? 0,
                placedAtUtc = o.PlacedAtUtc
            })
            .ToListAsync(ct);

        return Ok(new
        {
            totalBalance,
            totalPnl,
            totalPnlPercent = totalBalance > 0 ? Math.Round(totalPnl / totalBalance * 100, 2) : 0,
            openPositionCount,
            activeStrategyCount,
            dailyLossPercent = 0m,
            maxDrawdownPercent = 0m,
            riskStatus = "Normal",
            exchangeStatus,
            recentTrades
        });
    }
}
