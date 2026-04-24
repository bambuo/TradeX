using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/traders/{traderId:guid}/strategies")]
[Authorize]
public class StrategiesController(
    ITraderRepository traderRepo,
    IStrategyRepository strategyRepo) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid traderId, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var strategies = await strategyRepo.GetByTraderIdAsync(traderId, ct);
        return Ok(strategies.Select(s => new
        {
            s.Id, s.TraderId, s.Name, s.ExchangeId, s.SymbolIds,
            s.Timeframe, s.Status, s.Version, s.CreatedAtUtc, s.UpdatedAtUtc
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid traderId, Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null || strategy.TraderId != traderId)
            return NotFound(new { message = "策略不存在" });

        return Ok(new
        {
            strategy.Id, strategy.TraderId, strategy.Name, strategy.ExchangeId,
            strategy.SymbolIds, strategy.Timeframe, strategy.EntryConditionJson,
            strategy.ExitConditionJson, strategy.ExecutionRuleJson,
            strategy.Status, strategy.Version, strategy.CreatedAtUtc, strategy.UpdatedAtUtc
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid traderId, [FromBody] CreateStrategyRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "策略名称不能为空" });

        var strategy = new Strategy
        {
            TraderId = traderId,
            Name = request.Name,
            ExchangeId = request.ExchangeId,
            SymbolIds = request.SymbolIds ?? "[]",
            Timeframe = request.Timeframe ?? "15m",
            EntryConditionJson = request.EntryConditionJson ?? "{}",
            ExitConditionJson = request.ExitConditionJson ?? "{}",
            ExecutionRuleJson = request.ExecutionRuleJson ?? "{}",
            CreatedBy = UserId
        };

        await strategyRepo.AddAsync(strategy, ct);

        return CreatedAtAction(nameof(GetById), new { traderId, id = strategy.Id }, new
        {
            strategy.Id, strategy.TraderId, strategy.Name, strategy.ExchangeId,
            strategy.SymbolIds, strategy.Timeframe, strategy.Status, strategy.Version, strategy.CreatedAtUtc
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid traderId, Guid id, [FromBody] UpdateStrategyRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null || strategy.TraderId != traderId)
            return NotFound(new { message = "策略不存在" });

        if (strategy.Status == StrategyStatus.Active)
            return BadRequest(new { message = "活跃策略不可编辑，请先禁用" });

        if (request.Name is not null)
            strategy.Name = request.Name;
        if (request.SymbolIds is not null)
            strategy.SymbolIds = request.SymbolIds;
        if (request.Timeframe is not null)
            strategy.Timeframe = request.Timeframe;
        if (request.EntryConditionJson is not null)
            strategy.EntryConditionJson = request.EntryConditionJson;
        if (request.ExitConditionJson is not null)
            strategy.ExitConditionJson = request.ExitConditionJson;
        if (request.ExecutionRuleJson is not null)
            strategy.ExecutionRuleJson = request.ExecutionRuleJson;

        strategy.Version++;
        strategy.UpdatedAtUtc = DateTime.UtcNow;
        await strategyRepo.UpdateAsync(strategy, ct);

        return Ok(new
        {
            strategy.Id, strategy.TraderId, strategy.Name, strategy.ExchangeId,
            strategy.SymbolIds, strategy.Timeframe, strategy.Status, strategy.Version, strategy.UpdatedAtUtc
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid traderId, Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null || strategy.TraderId != traderId)
            return NotFound(new { message = "策略不存在" });

        if (strategy.Status == StrategyStatus.Active)
            return BadRequest(new { message = "活跃策略不可删除，请先禁用" });

        await strategyRepo.DeleteAsync(strategy, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid traderId, Guid id, [FromBody] ToggleStrategyRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null || strategy.TraderId != traderId)
            return NotFound(new { message = "策略不存在" });

        if (request.Enable)
        {
            if (strategy.Status == StrategyStatus.Draft)
                return BadRequest(new { message = "草稿策略必须先通过回测才能启用" });

            var symbolIds = strategy.SymbolIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var symbolId in symbolIds)
            {
                var hasConflict = await strategyRepo.ExistsActiveAsync(traderId, strategy.ExchangeId, symbolId, id, ct);
                if (hasConflict)
                    return Conflict(new { message = $"交易对 {symbolId} 上已有活跃策略" });
            }

            strategy.Status = StrategyStatus.Active;
        }
        else
        {
            strategy.Status = StrategyStatus.Disabled;
        }

        strategy.UpdatedAtUtc = DateTime.UtcNow;
        await strategyRepo.UpdateAsync(strategy, ct);

        return Ok(new { strategy.Id, strategy.Status, strategy.UpdatedAtUtc });
    }

    public record CreateStrategyRequest(
        string Name, Guid ExchangeId, string? SymbolIds = null, string? Timeframe = null,
        string? EntryConditionJson = null, string? ExitConditionJson = null, string? ExecutionRuleJson = null);

    public record UpdateStrategyRequest(
        string? Name = null, string? SymbolIds = null, string? Timeframe = null,
        string? EntryConditionJson = null, string? ExitConditionJson = null, string? ExecutionRuleJson = null);

    public record ToggleStrategyRequest(bool Enable);
}
