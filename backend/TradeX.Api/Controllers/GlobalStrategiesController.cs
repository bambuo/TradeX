using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.ErrorCodes;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading.Engine;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/strategies")]
[Authorize]
public class StrategiesController(
    IStrategyRepository strategyRepo,
    ConditionTreeValidator validator,
    IIndicatorRegistry indicatorRegistry) : ControllerBase
{
    /// <summary>前端可视化编辑器拉取可用指标列表 + 合法运算符, 用于条件树节点下拉.</summary>
    [HttpGet("schema")]
    [AllowAnonymous]
    public IActionResult GetSchema() => Ok(new
    {
        indicators = indicatorRegistry.RegisteredNames.OrderBy(n => n).ToArray(),
        comparisons = new[] { ">", "<", ">=", "<=", "==", "CrossAbove", "CrossBelow" },
        groupOperators = new[] { "AND", "OR", "NOT" }
    });

    /// <summary>前端实时校验条件树, 不持久化. 用于编辑器即时反馈错误.</summary>
    [HttpPost("validate")]
    public IActionResult ValidateConditionTree([FromBody] ValidateRequest request)
    {
        var entry = validator.Validate(request.EntryCondition ?? "{}");
        var exit = validator.Validate(request.ExitCondition ?? "{}");
        return Ok(new
        {
            valid = entry.IsValid && exit.IsValid,
            entryIssues = entry.Issues,
            exitIssues = exit.Issues
        });
    }

    public record ValidateRequest(string? EntryCondition, string? ExitCondition);

    private IActionResult? ValidateConditions(string? entry, string? exit)
    {
        var issues = new List<ValidationIssue>();
        if (entry is not null)
        {
            var r = validator.Validate(entry);
            if (!r.IsValid) issues.AddRange(r.Issues.Select(i => new ValidationIssue($"EntryCondition.{i.Path[2..]}", i.Message)));
        }
        if (exit is not null)
        {
            var r = validator.Validate(exit);
            if (!r.IsValid) issues.AddRange(r.Issues.Select(i => new ValidationIssue($"ExitCondition.{i.Path[2..]}", i.Message)));
        }
        return issues.Count == 0
            ? null
            : StatusCode(400, new { code = BusinessErrorCode.ValidationError, message = "条件树校验失败", issues });
    }
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var strategies = await strategyRepo.GetAllAsync(ct);
        return Ok(new
        {
            data = strategies.Select(s => new
            {
                s.Id, s.Name, s.EntryCondition, s.ExitCondition,
                s.ExecutionRule, s.Version, s.CreatedAt, s.UpdatedAt
            })
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null)
            return this.NotFound(BusinessErrorCode.StrategyNotFound, "策略不存在");

        return Ok(new
        {
            strategy.Id, strategy.Name, strategy.EntryCondition,
            strategy.ExitCondition, strategy.ExecutionRule,
            strategy.Version, strategy.CreatedAt, strategy.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStrategyRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return this.BadRequest(BusinessErrorCode.ValidationError, "策略名称不能为空");

        var validationError = ValidateConditions(request.EntryCondition, request.ExitCondition);
        if (validationError is not null) return validationError;

        var strategy = new Strategy
        {
            Name = request.Name,
            EntryCondition = request.EntryCondition ?? "{}",
            ExitCondition = request.ExitCondition ?? "{}",
            ExecutionRule = request.ExecutionRule ?? "{}"
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
            return this.NotFound(BusinessErrorCode.StrategyNotFound, "策略不存在");

        var validationError = ValidateConditions(request.EntryCondition, request.ExitCondition);
        if (validationError is not null) return validationError;

        if (request.Name is not null)
            strategy.Name = request.Name;
        if (request.EntryCondition is not null)
            strategy.EntryCondition = request.EntryCondition;
        if (request.ExitCondition is not null)
            strategy.ExitCondition = request.ExitCondition;
        if (request.ExecutionRule is not null)
            strategy.ExecutionRule = request.ExecutionRule;

        strategy.Version++;
        await strategyRepo.UpdateAsync(strategy, ct);

        return Ok(new { strategy.Id, strategy.Name, strategy.Version, strategy.UpdatedAt });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var strategy = await strategyRepo.GetByIdAsync(id, ct);
        if (strategy is null)
            return this.NotFound(BusinessErrorCode.StrategyNotFound, "策略不存在");

        await strategyRepo.DeleteAsync(strategy, ct);
        return NoContent();
    }

    public record CreateStrategyRequest(
        string Name, string? EntryCondition = null, string? ExitCondition = null, string? ExecutionRule = null);

    public record UpdateStrategyRequest(
        string? Name = null, string? EntryCondition = null, string? ExitCondition = null, string? ExecutionRule = null);
}
