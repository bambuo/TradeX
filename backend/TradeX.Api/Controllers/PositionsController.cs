using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Positions;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/traders/{traderId:guid}/positions")]
[Authorize]
public class PositionsController(
    IUseCase<GetOpenPositionsQuery, Result<List<PositionDto>>> getOpenPositions,
    IUseCase<GetPositionByIdQuery, Result<PositionDto>> getPositionById) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid traderId, [FromQuery] bool? openOnly, CancellationToken ct)
    {
        var query = new GetOpenPositionsQuery(traderId, UserId);
        var result = await getOpenPositions.ExecuteAsync(query, ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });

        return Ok(result.Data!.Select(p => new
        {
            p.Id, p.TraderId, p.Pair,
            p.Quantity, p.EntryPrice, p.CurrentPrice, p.UnrealizedPnl, p.RealizedPnl,
            p.Status, p.OpenedAt, p.ClosedAt
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid traderId, Guid id, CancellationToken ct)
    {
        var query = new GetPositionByIdQuery(traderId, id, UserId);
        var result = await getPositionById.ExecuteAsync(query, ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });

        var p = result.Data!;
        return Ok(new
        {
            p.Id, p.TraderId, p.Pair,
            p.Quantity, p.EntryPrice, p.CurrentPrice, p.UnrealizedPnl, p.RealizedPnl,
            p.Status, p.OpenedAt, p.ClosedAt
        });
    }
}
