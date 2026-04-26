using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/strategies")]
[Authorize]
public class StrategiesController(
    IStrategyRepository strategyRepo) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var strategies = await strategyRepo.GetAllAsync(ct);
        return Ok(new
        {
            data = strategies.Select(s => new
            {
                s.Id, s.Name, s.EntryConditionJson, s.ExitConditionJson,
                s.ExecutionRuleJson, s.Version, s.CreatedAt, s.UpdatedAt
            })
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null)
            return NotFound(new { code = "STRATEGY_NOT_FOUND", message = "策略不存在" });

        return Ok(new
        {
            strategy.Id, strategy.Name, strategy.EntryConditionJson,
            strategy.ExitConditionJson, strategy.ExecutionRuleJson,
            strategy.Version, strategy.CreatedAt, strategy.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStrategyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { code = "VALIDATION_ERROR", message = "策略名称不能为空" });

        var strategy = new Strategy
        {
            Name = request.Name,
            EntryConditionJson = request.EntryConditionJson ?? "{}",
            ExitConditionJson = request.ExitConditionJson ?? "{}",
            ExecutionRuleJson = request.ExecutionRuleJson ?? "{}"
        };

        await strategyRepo.AddAsync(strategy, ct);

        return CreatedAtAction(nameof(GetById), new { id = strategy.Id }, new
        {
            strategy.Id, strategy.Name, strategy.Version, strategy.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStrategyRequest request, CancellationToken ct)
    {
        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null)
            return NotFound(new { code = "STRATEGY_NOT_FOUND", message = "策略不存在" });

        if (request.Name is not null)
            strategy.Name = request.Name;
        if (request.EntryConditionJson is not null)
            strategy.EntryConditionJson = request.EntryConditionJson;
        if (request.ExitConditionJson is not null)
            strategy.ExitConditionJson = request.ExitConditionJson;
        if (request.ExecutionRuleJson is not null)
            strategy.ExecutionRuleJson = request.ExecutionRuleJson;

        strategy.Version++;
        await strategyRepo.UpdateAsync(strategy, ct);

        return Ok(new { strategy.Id, strategy.Name, strategy.Version, strategy.UpdatedAt });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null)
            return NotFound(new { code = "STRATEGY_NOT_FOUND", message = "策略不存在" });

        await strategyRepo.DeleteAsync(strategy, ct);
        return NoContent();
    }

    public record CreateStrategyRequest(
        string Name, string? EntryConditionJson = null, string? ExitConditionJson = null, string? ExecutionRuleJson = null);

    public record UpdateStrategyRequest(
        string? Name = null, string? EntryConditionJson = null, string? ExitConditionJson = null, string? ExecutionRuleJson = null);
}
