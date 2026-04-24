using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TradersController(ITraderRepository traderRepo) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var traders = await traderRepo.GetByUserIdAsync(UserId, ct);
        var result = traders.Select(t => new
        {
            t.Id, t.Name, t.Status, t.CreatedAt, t.UpdatedAt
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(id, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        return Ok(new
        {
            trader.Id, trader.Name, trader.Status, trader.CreatedAt, trader.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateTraderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "名称不能为空" });

        var isUnique = await traderRepo.IsNameUniqueAsync(UserId, request.Name, ct);
        if (!isUnique)
            return Conflict(new { message = "交易员名称已存在" });

        var trader = new Trader
        {
            UserId = UserId,
            Name = request.Name
        };

        await traderRepo.AddAsync(trader, ct);

        return CreatedAtAction(nameof(GetById), new { id = trader.Id }, new
        {
            trader.Id, trader.Name, trader.Status, trader.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTraderRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(id, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var isUnique = await traderRepo.IsNameUniqueAsync(UserId, request.Name, ct);
            if (!isUnique)
                return Conflict(new { message = "交易员名称已存在" });
            trader.Name = request.Name;
        }

        await traderRepo.UpdateAsync(trader, ct);

        return Ok(new { trader.Id, trader.Name, trader.Status, trader.UpdatedAt });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(id, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        await traderRepo.DeleteAsync(trader, ct);

        return NoContent();
    }

    public record CreateTraderRequest(string Name);

    public record UpdateTraderRequest(string? Name);
}
