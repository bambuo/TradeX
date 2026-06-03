using TradeX.Application.Common;
using TradeX.Application.Traders.DTOs;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Application.Traders;

/// <summary>
/// 查询交易员列表用例。
/// </summary>
public sealed class GetTradersUseCase(
    ITraderRepository traderRepo) : IUseCase<GetTradersQuery, Result<List<TraderDto>>>
{
    public async Task<Result<List<TraderDto>>> ExecuteAsync(GetTradersQuery query, CancellationToken ct = default)
    {
        var traders = await traderRepo.GetByUserIdAsync(query.CurrentUserId, ct);
        var dtos = traders.Select(t => new TraderDto(
            t.Id, t.Name, t.Status.ToString(),
            t.AvatarColor, t.AvatarUrl, t.Style,
            t.CreatedAt, t.UpdatedAt)).ToList();
        return Result<List<TraderDto>>.Ok(dtos);
    }
}

public sealed record GetTradersQuery(Guid CurrentUserId);

/// <summary>
/// 创建交易员用例。使用 <see cref="Trader.Create"/> 工厂方法。
/// </summary>
public sealed class CreateTraderUseCase(
    ITraderRepository traderRepo) : IUseCase<CreateTraderCommand, Result<TraderDto>>
{
    public async Task<Result<TraderDto>> ExecuteAsync(CreateTraderCommand cmd, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            return Result<TraderDto>.BadRequest("名称不能为空");

        var isUnique = await traderRepo.IsNameUniqueAsync(cmd.CurrentUserId, cmd.Name, null, ct);
        if (!isUnique)
            return Result<TraderDto>.Conflict("交易员名称已存在");

        var trader = Trader.Create(cmd.CurrentUserId, cmd.Name, cmd.AvatarColor, cmd.Style);
        await traderRepo.AddAsync(trader, ct);

        return Result<TraderDto>.Created(new TraderDto(
            trader.Id, trader.Name, trader.Status.ToString(),
            trader.AvatarColor, trader.AvatarUrl, trader.Style,
            trader.CreatedAt, trader.UpdatedAt));
    }
}

public sealed record CreateTraderCommand(
    Guid CurrentUserId,
    string Name,
    string? AvatarColor = null,
    string? Style = null);

/// <summary>
/// 获取交易员统计数据用例。
/// </summary>
public sealed class GetTraderStatsUseCase(
    ITraderRepository traderRepo,
    IOrderRepository orderRepo) : IUseCase<GetTraderStatsQuery, Result<TraderStatsDto>>
{
    public async Task<Result<TraderStatsDto>> ExecuteAsync(GetTraderStatsQuery query, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(query.TraderId, ct);
        if (trader is null || trader.UserId != query.CurrentUserId)
            return Result<TraderStatsDto>.NotFound("交易员不存在");

        var filledOrders = (await orderRepo.GetByTraderIdAsync(query.TraderId, ct))
            .Where(o => o.Status == OrderStatus.Filled && o.QuoteQuantity > 0)
            .ToList();

        var totalTrades = filledOrders.Count;
        var wins = filledOrders.Count(o => o.Side == OrderSide.Sell && (o.FilledQuantity * (o.Price ?? 0)) > o.QuoteQuantity);
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
        var sharpeRatio = totalTrades > 1 ? (decimal)Math.Round((double)winRate / 100 * Math.Sqrt(365), 2) : 0m;

        return Result<TraderStatsDto>.Ok(new TraderStatsDto(totalTrades, winRate, profitLossRatio, sharpeRatio));
    }
}

public sealed record GetTraderStatsQuery(Guid TraderId, Guid CurrentUserId);
