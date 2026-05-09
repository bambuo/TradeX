using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/traders/{traderId:guid}/orders")]
[Authorize]
public class OrdersController(
    ITraderRepository traderRepo,
    IOrderRepository orderRepo) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid traderId, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var orders = await orderRepo.GetByTraderIdAsync(traderId, ct);
        return Ok(orders.Select(o => new
        {
            o.Id, o.TraderId, o.ExchangeOrderId, o.ExchangeId, o.StrategyId, o.PositionId,
            o.Pair, o.Side, o.Type, o.Status, o.Price, o.Quantity, o.FilledQuantity,
            o.QuoteQuantity, o.Fee, o.FeeAsset, o.IsManual, o.PlacedAtUtc, o.UpdatedAt
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid traderId, Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var order = await orderRepo.GetByIdAsync(id, ct);
        if (order is null || order.TraderId != traderId)
            return NotFound(new { message = "订单不存在" });

        return Ok(new
        {
            order.Id, order.TraderId, order.ExchangeOrderId, order.ExchangeId, order.StrategyId, order.PositionId,
            order.Pair, order.Side, order.Type, order.Status, order.Price, order.Quantity, order.FilledQuantity,
            order.QuoteQuantity, order.Fee, order.FeeAsset, order.IsManual, order.PlacedAtUtc, order.UpdatedAt
        });
    }

    [HttpPost("manual")]
    public async Task<IActionResult> CreateManual(Guid traderId, [FromBody] CreateManualOrderRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        if (!Enum.TryParse<OrderSide>(request.Side, true, out var side))
            return BadRequest(new { message = $"无效的订单方向: {request.Side}" });

        if (!Enum.TryParse<OrderType>(request.Type, true, out var type))
            return BadRequest(new { message = $"无效的订单类型: {request.Type}" });

        var order = new Order
        {
            TraderId = traderId,
            ExchangeId = request.ExchangeId,
            StrategyId = request.StrategyId,
            Pair = request.Pair,
            Side = side,
            Type = type,
            Price = request.Price,
            Quantity = request.Quantity,
            IsManual = true
        };

        await orderRepo.AddAsync(order, ct);

        return CreatedAtAction(nameof(GetById), new { traderId, id = order.Id }, new
        {
            order.Id, order.TraderId, order.ExchangeId, order.StrategyId,
            order.Pair, order.Side, order.Type, order.Status,
            order.Price, order.Quantity, order.IsManual, order.PlacedAtUtc
        });
    }

    public record CreateManualOrderRequest(Guid ExchangeId, string Pair, string Side, string Type, decimal Quantity, decimal? Price = null, Guid? StrategyId = null);
}
