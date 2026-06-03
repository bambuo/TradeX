using TradeX.Application.Common;
using TradeX.Application.Orders.DTOs;
using TradeX.Core.Interfaces;

namespace TradeX.Application.Orders;

/// <summary>
/// 查询交易员订单列表 — 应用服务用例。
/// </summary>
public sealed class GetTraderOrdersUseCase(
    ITraderRepository traderRepo,
    IOrderRepository orderRepo) : IUseCase<GetTraderOrdersQuery, Result<List<OrderDto>>>
{
    public async Task<Result<List<OrderDto>>> ExecuteAsync(GetTraderOrdersQuery query, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(query.TraderId, ct);
        if (trader is null || trader.UserId != query.CurrentUserId)
            return Result<List<OrderDto>>.NotFound("交易员不存在");

        var orders = await orderRepo.GetByTraderIdAsync(query.TraderId, ct);
        var dtos = orders.Select(MapToDto).ToList();
        return Result<List<OrderDto>>.Ok(dtos);
    }

    private static OrderDto MapToDto(Core.Models.Order o) => new(
        o.Id, o.TraderId, o.Pair,
        o.Side.ToString(), o.Type.ToString(), o.Status.ToString(),
        o.Quantity, o.FilledQuantity, o.Price, o.QuoteQuantity,
        o.Fee, o.FeeAsset, o.IsManual, o.PlacedAtUtc, o.UpdatedAt);
}

public sealed record GetTraderOrdersQuery(Guid TraderId, Guid CurrentUserId);
