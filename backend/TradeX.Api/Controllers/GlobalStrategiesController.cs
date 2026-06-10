using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Strategies;
using TradeX.Core.ErrorCodes;
using TradeX.Indicators;
using TradeX.Trading.Engine;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/strategies")]
[Authorize]
public class StrategiesController(
    ConditionTreeValidator validator,
    IIndicatorRegistry indicatorRegistry,
    IUseCase<GetStrategiesQuery, Result<List<StrategyDto>>> getStrategies,
    IUseCase<GetStrategyByIdQuery, Result<StrategyDto>> getStrategyById,
    IUseCase<CreateStrategyCommand, Result<StrategyDto>> createStrategy,
    IUseCase<UpdateStrategyCommand, Result<StrategyDto>> updateStrategy,
    IUseCase<DeleteStrategyCommand, Result> deleteStrategy) : ControllerBase
{
    /// <summary>前端可视化编辑器拉取可用指标列表 + 合法运算符, 用于条件树节点下拉.</summary>
    [HttpGet("schema")]
    [AllowAnonymous]
    public IActionResult GetSchema() => Ok(new
    {
        indicators = indicatorRegistry.RegisteredNames.OrderBy(n => n).ToArray(),
        comparisons = new[] { ">", "<", ">=", "<=", "==", "CA", "CB" },
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
            : StatusCode(400, new { code = 1000, message = "条件树校验失败", issues });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await getStrategies.ExecuteAsync(new GetStrategiesQuery(), ct);
        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(new
        {
            data = result.Data!.Select(s => new
            {
                s.Id, s.Name, s.EntryCondition, s.ExitCondition,
                s.Version, s.CreatedAt, s.UpdatedAt
            })
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await getStrategyById.ExecuteAsync(new GetStrategyByIdQuery(id), ct);
        if (!result.Success)
            return NotFound(new { error = "策略不存在" });

        var s = result.Data!;
        return Ok(new
        {
            s.Id, s.Name, s.EntryCondition, s.ExitCondition,
            s.Version, s.CreatedAt, s.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStrategyRequest request, CancellationToken ct)
    {
        var validationError = ValidateConditions(request.EntryCondition, request.ExitCondition);
        if (validationError is not null) return validationError;

        var cmd = new CreateStrategyCommand(
            request.Name, request.EntryCondition, request.ExitCondition, request.ExecutionRule);
        var result = await createStrategy.ExecuteAsync(cmd, ct);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        var s = result.Data!;
        return CreatedAtAction(nameof(GetById), new { id = s.Id }, new
        {
            s.Id, s.Name, s.Version, s.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStrategyRequest request, CancellationToken ct)
    {
        var validationError = ValidateConditions(request.EntryCondition, request.ExitCondition);
        if (validationError is not null) return validationError;

        var cmd = new UpdateStrategyCommand(
            id, request.Name, request.EntryCondition, request.ExitCondition, request.ExecutionRule);
        var result = await updateStrategy.ExecuteAsync(cmd, ct);

        if (!result.Success)
            return NotFound(new { error = result.Error });

        var s = result.Data!;
        return Ok(new { s.Id, s.Name, s.Version, s.UpdatedAt });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await deleteStrategy.ExecuteAsync(new DeleteStrategyCommand(id), ct);
        if (!result.Success)
            return NotFound(new { error = result.Error });

        return NoContent();
    }

    public record CreateStrategyRequest(
        string Name, string? EntryCondition = null, string? ExitCondition = null, string? ExecutionRule = null);

    public record UpdateStrategyRequest(
        string? Name = null, string? EntryCondition = null, string? ExitCondition = null, string? ExecutionRule = null);
}
