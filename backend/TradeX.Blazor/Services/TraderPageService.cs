using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data;

namespace TradeX.Blazor.Services;

public sealed class TraderPageService(
    ITraderRepository traderRepo,
    TradeXDbContext dbContext) : ITraderPageService
{
    public async Task<IReadOnlyList<TraderItem>> GetAllAsync(ClaimsPrincipal user, CancellationToken ct = default)
    {
        var userId = GetUserId(user);
        var traders = await traderRepo.GetByUserIdAsync(userId, ct);
        return traders
            .OrderBy(t => t.Name)
            .Select(t => new TraderItem(
                t.Id, t.Name, t.Status, t.AvatarColor, t.AvatarUrl, t.Style, t.CreatedAt, t.UpdatedAt))
            .ToArray();
    }

    public async Task<TraderStatsView> GetStatsAsync(Guid id, CancellationToken ct = default)
    {
        var filledOrders = await dbContext.Orders
            .Where(o => o.TraderId == id && o.Status == OrderStatus.Filled && o.QuoteQuantity > 0)
            .ToListAsync(ct);

        var totalTrades = filledOrders.Count;
        var wins = filledOrders.Count(o =>
            o.Side == OrderSide.Sell && (o.FilledQuantity * (o.Price ?? 0)) > o.QuoteQuantity);
        var winRate = totalTrades > 0 ? Math.Round((decimal)wins / totalTrades * 100, 1) : 0m;

        var profitableTrades = filledOrders
            .Where(o => o.Side == OrderSide.Sell && (o.FilledQuantity * (o.Price ?? 0)) > o.QuoteQuantity)
            .Select(o => Math.Abs((o.FilledQuantity * (o.Price ?? 0)) - o.QuoteQuantity))
            .ToList();

        var losingTrades = filledOrders
            .Where(o => o.Side == OrderSide.Sell && (o.FilledQuantity * (o.Price ?? 0)) <= o.QuoteQuantity)
            .Select(o => Math.Abs((o.FilledQuantity * (o.Price ?? 0)) - o.QuoteQuantity))
            .ToList();

        var avgWin = profitableTrades.Count > 0 ? profitableTrades.Average() : 0m;
        var avgLoss = losingTrades.Count > 0 ? losingTrades.Average() : 0m;
        var profitLossRatio = avgLoss > 0 ? Math.Round(avgWin / avgLoss, 2) : 0m;

        var sharpeRatio = totalTrades > 1
            ? (decimal)Math.Round((double)winRate / 100 * Math.Sqrt(365), 2)
            : 0m;

        return new TraderStatsView(totalTrades, winRate, profitLossRatio, sharpeRatio);
    }

    public async Task<TraderItem> CreateAsync(ClaimsPrincipal user, TraderFormModel form, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(form.Name))
            throw new InvalidOperationException("名称不能为空");

        var userId = GetUserId(user);
        if (!await traderRepo.IsNameUniqueAsync(userId, form.Name, null, ct))
            throw new InvalidOperationException("交易员名称已存在");

        var trader = new Trader
        {
            UserId = userId,
            Name = form.Name,
            AvatarColor = form.AvatarColor,
            Style = form.Style
        };

        await traderRepo.AddAsync(trader, ct);

        return new TraderItem(
            trader.Id, trader.Name, trader.Status,
            trader.AvatarColor, trader.AvatarUrl, trader.Style,
            trader.CreatedAt, trader.UpdatedAt);
    }

    public async Task UpdateAsync(Guid id, TraderFormModel form, CancellationToken ct = default)
    {
        var trader = await GetTraderAsync(id, ct);

        if (!string.IsNullOrWhiteSpace(form.Name))
            trader.Name = form.Name;

        trader.AvatarColor = form.AvatarColor;
        trader.Style = form.Style;

        await traderRepo.UpdateAsync(trader, ct);
    }

    public async Task ToggleAsync(Guid id, bool enable, CancellationToken ct = default)
    {
        var trader = await GetTraderAsync(id, ct);
        trader.Status = enable ? TraderStatus.Active : TraderStatus.Disabled;
        await traderRepo.UpdateAsync(trader, ct);
    }

    private async Task<Trader> GetTraderAsync(Guid id, CancellationToken ct)
    {
        return await traderRepo.GetByIdAsync(id, ct)
            ?? throw new InvalidOperationException("交易员不存在");
    }

    private static Guid GetUserId(ClaimsPrincipal user)
    {
        var value = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out var userId)
            ? userId
            : throw new UnauthorizedAccessException("用户未登录");
    }
}

public sealed record TraderItem(
    Guid Id,
    string Name,
    TraderStatus Status,
    string? AvatarColor,
    string? AvatarUrl,
    string? Style,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record TraderStatsView(
    int TotalTrades,
    decimal WinRate,
    decimal ProfitLossRatio,
    decimal SharpeRatio);

public sealed class TraderFormModel
{
    public string Name { get; set; } = string.Empty;
    public string? AvatarColor { get; set; }
    public string? Style { get; set; }
}
