using TradeX.Application.Common;
using TradeX.Application.Orders.DTOs;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;

namespace TradeX.Application.Orders;

/// <summary>
/// 创建手动订单用例。
/// 使用 <see cref="Core.Models.Order.CreateManual"/> 工厂方法创建订单对象。
/// </summary>
public sealed class CreateManualOrderUseCase(
    ITraderRepository traderRepo,
    IOrderRepository orderRepo) : IUseCase<CreateManualOrderCommand, Result<OrderDto>>
{
    public async Task<Result<OrderDto>> ExecuteAsync(CreateManualOrderCommand cmd, CancellationToken ct = default)
    {
        var trader = await traderRepo.GetByIdAsync(cmd.TraderId, ct);
        if (trader is null || trader.UserId != cmd.CurrentUserId)
            return Result<OrderDto>.NotFound("交易员不存在");

        if (!Enum.TryParse<OrderSide>(cmd.Side, true, out var side))
            return Result<OrderDto>.BadRequest($"无效的订单方向: {cmd.Side}");

        if (!Enum.TryParse<OrderType>(cmd.Type, true, out var type))
            return Result<OrderDto>.BadRequest($"无效的订单类型: {cmd.Type}");

        // 使用工厂方法创建订单
        var order = Core.Models.Order.CreateManual(
            cmd.TraderId, cmd.ExchangeId, cmd.Pair,
            side, type, cmd.Quantity, cmd.Price, cmd.StrategyId);

        await orderRepo.AddAsync(order, ct);

        return Result<OrderDto>.Created(new OrderDto(
            order.Id, order.TraderId, order.Pair,
            order.Side.ToString(), order.Type.ToString(), order.Status.ToString(),
            order.Quantity, order.FilledQuantity, order.Price, order.QuoteQuantity,
            order.Fee, order.FeeAsset, order.IsManual, order.PlacedAtUtc, order.UpdatedAt));
    }
}

public sealed record CreateManualOrderCommand(
    Guid TraderId,
    Guid CurrentUserId,
    Guid ExchangeId,
    string Pair,
    string Side,
    string Type,
    decimal Quantity,
    decimal? Price = null,
    Guid? StrategyId = null);
