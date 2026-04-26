using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/traders/{traderId:guid}/positions")]
[Authorize]
public class PositionsController(
    ITraderRepository traderRepo,
    IPositionRepository positionRepo) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid traderId, [FromQuery] bool? openOnly, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var positions = openOnly == true
            ? await positionRepo.GetOpenByTraderIdAsync(traderId, ct)
            : await positionRepo.GetByStrategyIdAsync(traderId, ct);

        return Ok(positions.Select(p => new
        {
            p.Id, p.TraderId, p.ExchangeId, p.StrategyId, p.SymbolId,
            p.Quantity, p.EntryPrice, p.CurrentPrice, p.UnrealizedPnl, p.RealizedPnl,
            p.Status, p.OpenedAtUtc, p.ClosedAtUtc, p.UpdatedAt
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid traderId, Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var position = await positionRepo.GetByIdAsync(id, ct);
        if (position is null || position.TraderId != traderId)
            return NotFound(new { message = "持仓不存在" });

        return Ok(new
        {
            position.Id, position.TraderId, position.ExchangeId, position.StrategyId, position.SymbolId,
            position.Quantity, position.EntryPrice, position.CurrentPrice, position.UnrealizedPnl, position.RealizedPnl,
            position.Status, position.OpenedAtUtc, position.ClosedAtUtc, position.UpdatedAt
        });
    }
}
