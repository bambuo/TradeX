using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Strategies;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/strategies")]
[Authorize]
public class StrategiesController(
    IUseCase<GetStrategiesQuery, Result<List<StrategyDto>>> getStrategies,
    IUseCase<GetStrategyByIdQuery, Result<StrategyDto>> getStrategyById,
    IUseCase<CreateStrategyCommand, Result<StrategyDto>> createStrategy,
    IUseCase<UpdateStrategyCommand, Result<StrategyDto>> updateStrategy,
    IUseCase<DeleteStrategyCommand, Result> deleteStrategy) : ControllerBase
{
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
                s.Id, s.Name, s.Version, s.CreatedAt, s.UpdatedAt
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
            s.Id, s.Name, s.Version, s.CreatedAt, s.UpdatedAt
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateStrategyRequest request, CancellationToken ct)
    {
        var cmd = new CreateStrategyCommand(request.Name);
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
        var cmd = new UpdateStrategyCommand(id, request.Name);
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

    public record CreateStrategyRequest(string Name);

    public record UpdateStrategyRequest(string? Name = null);
}
