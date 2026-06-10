using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Strategies;
using TradeX.Indicators;
using TradeX.Rules.Indicators;
using TradeX.Trading.Engine;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/strategies")]
[Authorize]
public class StrategiesController(
    RuleSetValidator ruleSetValidator,
    IIndicatorRegistry indicatorRegistry,
    IUseCase<GetStrategiesQuery, Result<List<StrategyDto>>> getStrategies,
    IUseCase<GetStrategyByIdQuery, Result<StrategyDto>> getStrategyById,
    IUseCase<CreateStrategyCommand, Result<StrategyDto>> createStrategy,
    IUseCase<UpdateStrategyCommand, Result<StrategyDto>> updateStrategy,
    IUseCase<DeleteStrategyCommand, Result> deleteStrategy) : ControllerBase
{
    /// <summary>前端可视化编辑器拉取可用指标列表 + 合法运算符/动作，用于规则集编辑。</summary>
    [HttpGet("schema")]
    [AllowAnonymous]
    public IActionResult GetSchema() => Ok(new
    {
        indicators = indicatorRegistry.RegisteredNames.OrderBy(n => n).ToArray(),
        contextIndicators = ContextIndicators.All.OrderBy(n => n).ToArray(),
        comparisons = new[] { ">", "<", ">=", "<=", "==", "CA", "CB" },
        groupOperators = new[] { "AND", "OR", "NOT", "TRUE" },
        actions = new[] { "buy", "sell", "sellAll", "hold" },
        contexts = new[] { "any", "noPosition", "hasPosition" },
        sizeTypes = new[] { "fixed", "multiplier" }
    });

    /// <summary>前端实时校验执行规则集, 不持久化. 用于编辑器即时反馈错误.</summary>
    [HttpPost("validate")]
    public IActionResult ValidateRuleSet([FromBody] ValidateRequest request)
    {
        var result = ruleSetValidator.Validate(request.ExecutionRule ?? "{}");
        return Ok(new { valid = result.IsValid, issues = result.Issues });
    }

    public record ValidateRequest(string? ExecutionRule);

    /// <summary>校验执行规则集；非空且不合法时返回 400。空规则集（"{}"）视为草稿，允许保存。</summary>
    private IActionResult? ValidateExecutionRule(string? executionRule)
    {
        if (string.IsNullOrWhiteSpace(executionRule) || executionRule == "{}")
            return null;

        var result = ruleSetValidator.Validate(executionRule);
        return result.IsValid
            ? null
            : StatusCode(400, new { code = 1000, message = "执行规则集校验失败", issues = result.Issues });
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
                s.Id, s.Name, s.ExecutionRule,
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
            s.Id, s.Name, s.ExecutionRule,
            s.Version, s.CreatedAt, s.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStrategyRequest request, CancellationToken ct)
    {
        var validationError = ValidateExecutionRule(request.ExecutionRule);
        if (validationError is not null) return validationError;

        var cmd = new CreateStrategyCommand(request.Name, request.ExecutionRule);
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
        var validationError = ValidateExecutionRule(request.ExecutionRule);
        if (validationError is not null) return validationError;

        var cmd = new UpdateStrategyCommand(id, request.Name, request.ExecutionRule);
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
            return NotFound(new { error = "策略不存在" });

        return NoContent();
    }

    public record CreateStrategyRequest(string Name, string? ExecutionRule = null);

    public record UpdateStrategyRequest(string? Name = null, string? ExecutionRule = null);
}
