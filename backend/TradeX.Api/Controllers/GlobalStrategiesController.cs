using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Strategies;
using TradeX.Core.Enums;
using TradeX.Core.ErrorCodes;
using TradeX.Trading.Rules;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/strategies")]
public class StrategiesController(
    IUseCase<GetStrategiesQuery, Result<List<StrategyDto>>> getStrategies,
    IUseCase<GetStrategyByIdQuery, Result<StrategyDto>> getStrategyById,
    IUseCase<CreateStrategyCommand, Result<StrategyDto>> createStrategy,
    IUseCase<UpdateStrategyCommand, Result<StrategyDto>> updateStrategy,
    IUseCase<DeleteStrategyCommand, Result> deleteStrategy,
    NodeRegistry nodeRegistry) : ControllerBase
{
    /// <summary>获取规则链 schema（节点类型清单 + 阶段定义），公开无需认证。</summary>
    [AllowAnonymous]
    [HttpGet("schema")]
    public IActionResult GetSchema()
    {
        var nodes = nodeRegistry.ListAll().Select(d => new
        {
            d.Kind, d.Phase, d.Category, d.Description,
            d.AllowDuplicate, d.ProducesDecisions, d.EmitNames, d.EmitScope,
            Params = d.Params.Select(p => new
            {
                p.Name, p.Type, p.Required, p.Default, p.Min, p.Max,
                p.Enum, p.Description, p.RefScope, p.Unit
            }),
            Examples = d.Examples
        });

        var phases = Enum.GetValues<RulePhase>().Select(p => new
        {
            value = (int)p,
            name = p.ToString(),
            label = p switch
            {
                RulePhase.Gate => "条件门",
                RulePhase.Filter => "过滤",
                RulePhase.Derive => "派生",
                RulePhase.Size => "仓位",
                RulePhase.Action => "决策",
                RulePhase.Risk => "风控",
                RulePhase.Override => "覆盖",
                _ => p.ToString()
            }
        });

        return Ok(ApiResponse.Ok(new { nodes, phases }));
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await getStrategies.ExecuteAsync(new GetStrategiesQuery(), ct);
        if (!result.Success)
            return BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, result.Error!));

        return Ok(ApiResponse.Ok(new
        {
            data = result.Data!.Select(s => new
            {
                s.Id, s.Name, s.Version, s.CreatedAt, s.UpdatedAt, s.ChainsJson
            })
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await getStrategyById.ExecuteAsync(new GetStrategyByIdQuery(id), ct);
        if (!result.Success)
            return NotFound(ApiResponse.Error(BusinessErrorCode.NotFound, "策略不存在"));

        var s = result.Data!;
        return Ok(ApiResponse.Ok(new
        {
            s.Id, s.Name, s.Version, s.CreatedAt, s.UpdatedAt, s.ChainsJson
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStrategyRequest request, CancellationToken ct)
    {
        var cmd = new CreateStrategyCommand(request.Name, request.Chains);
        var result = await createStrategy.ExecuteAsync(cmd, ct);

        if (!result.Success)
            return BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, result.Error!));

        var s = result.Data!;
        return CreatedAtAction(nameof(GetById), new { id = s.Id }, ApiResponse.Ok(new
        {
            s.Id, s.Name, s.Version, s.CreatedAt, s.ChainsJson
        }));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateStrategyRequest request, CancellationToken ct)
    {
        var cmd = new UpdateStrategyCommand(id, request.Name, request.Chains);
        var result = await updateStrategy.ExecuteAsync(cmd, ct);

        if (!result.Success)
            return NotFound(ApiResponse.Error(BusinessErrorCode.NotFound, result.Error!));

        var s = result.Data!;
        return Ok(ApiResponse.Ok(new { s.Id, s.Name, s.Version, s.UpdatedAt, s.ChainsJson }));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await deleteStrategy.ExecuteAsync(new DeleteStrategyCommand(id), ct);
        if (!result.Success)
            return NotFound(ApiResponse.Error(BusinessErrorCode.NotFound, "策略不存在"));

        return NoContent();
    }

    public record CreateStrategyRequest(string Name, string? Chains = null);

    public record UpdateStrategyRequest(string? Name = null, string? Chains = null);
}
