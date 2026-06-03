using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Orders;
using TradeX.Application.Orders.DTOs;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/traders/{traderId:guid}/orders")]
[Authorize]
public class OrdersController(
    IUseCase<GetTraderOrdersQuery, Result<List<OrderDto>>> getOrders,
    IUseCase<CreateManualOrderCommand, Result<OrderDto>> createOrder) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid traderId, CancellationToken ct)
    {
        var result = await getOrders.ExecuteAsync(new GetTraderOrdersQuery(traderId, UserId), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid traderId, Guid id, CancellationToken ct)
    {
        var result = await getOrders.ExecuteAsync(new GetTraderOrdersQuery(traderId, UserId), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });

        var order = result.Data!.FirstOrDefault(o => o.Id == id);
        if (order is null)
            return NotFound(new { message = "订单不存在" });

        return Ok(order);
    }

    [HttpPost("manual")]
    public async Task<IActionResult> CreateManual(Guid traderId, [FromBody] CreateManualOrderRequest request, CancellationToken ct)
    {
        var result = await createOrder.ExecuteAsync(
            new CreateManualOrderCommand(
                traderId, UserId, request.ExchangeId, request.Pair,
                request.Side, request.Type, request.Quantity,
                request.Price, request.StrategyId), ct);

        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        return CreatedAtAction(nameof(GetById), new { traderId, id = result.Data!.Id }, result.Data);
    }

    public record CreateManualOrderRequest(Guid ExchangeId, string Pair, string Side, string Type, decimal Quantity, decimal? Price = null, Guid? StrategyId = null);
}
