using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Api.Filters;
using TradeX.Application.Common;
using TradeX.Application.StrategyBindings;
using TradeX.Trading.Commands;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/traders/{traderId:guid}/strategies")]
[Authorize]
public class StrategyBindingsController(
    IUseCase<GetBindingsQuery, Result<List<BindingDto>>> getBindings,
    IUseCase<GetBindingByIdQuery, Result<BindingDto>> getBindingById,
    IUseCase<CreateBindingCommand, Result<BindingDto>> createBinding,
    IUseCase<UpdateBindingCommand, Result<BindingDto>> updateBinding,
    IUseCase<DeleteBindingCommand, Result> deleteBinding,
    IUseCase<ActivateBindingCommand, Result<BindingDto>> activateBinding,
    IUseCase<DeactivateBindingCommand, Result<BindingDto>> deactivateBinding,
    IWorkerCommandPublisher commandPublisher) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private static string Fmt(DateTime dt) =>
        dt.Kind == DateTimeKind.Utc
            ? dt.ToString("yyyy-MM-dd HH:mm:ss")
            : DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss");

    private static string ResolveScope(string pairs, Guid exchangeId)
    {
        var hasPairs = !string.IsNullOrWhiteSpace(pairs)
            && pairs != "[]"
            && pairs.Replace("\"", "").Replace("[", "").Replace("]", "").Trim().Length > 0;
        if (hasPairs) return "Pair";
        if (exchangeId != Guid.Empty) return "Exchange";
        return "Trader";
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid traderId, CancellationToken ct)
    {
        var result = await getBindings.ExecuteAsync(new GetBindingsQuery(traderId, UserId), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });

        return Ok(result.Data!.Select(d => new
        {
            d.Id, d.StrategyId, traderId, scope = ResolveScope(d.Pairs, Guid.Empty),
            d.Name, d.Pairs, d.Timeframe, d.Status, d.CreatedAt
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid traderId, Guid id, CancellationToken ct)
    {
        var result = await getBindingById.ExecuteAsync(new GetBindingByIdQuery(id, traderId, UserId), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });

        var d = result.Data!;
        return Ok(new
        {
            d.Id, d.StrategyId, d.Name, traderId,
            scope = ResolveScope(d.Pairs, Guid.Empty),
            d.Pairs, d.Timeframe, d.Status,
            CreatedAt = Fmt(d.CreatedAt)
        });
    }

    [HttpPost]
    [RequireMfa]
    public async Task<IActionResult> Create(Guid traderId, [FromBody] CreateBindingRequest request, CancellationToken ct)
    {
        var exchangeId = request.ExchangeId ?? Guid.Empty;
        var pairs = request.Pairs ?? "[]";
        var timeframe = request.Timeframe ?? "15m";
        var name = request.Name ?? "未知策略";

        var result = await createBinding.ExecuteAsync(new CreateBindingCommand(
            traderId, UserId, request.StrategyId, exchangeId, pairs, timeframe, name), ct);

        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        var d = result.Data!;
        return CreatedAtAction(nameof(GetById), new { traderId, id = d.Id }, new
        {
            d.Id, d.StrategyId, traderId,
            scope = ResolveScope(d.Pairs, Guid.Empty),
            d.Pairs, d.Timeframe, d.Status,
            CreatedAt = Fmt(d.CreatedAt)
        });
    }

    [HttpPut("{id:guid}")]
    [RequireMfa]
    public async Task<IActionResult> Update(Guid traderId, Guid id, [FromBody] UpdateBindingRequest request, CancellationToken ct)
    {
        var result = await updateBinding.ExecuteAsync(new UpdateBindingCommand(
            id, traderId, UserId, request.Pairs, request.Timeframe, request.Name), ct);

        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                409 => Conflict(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        var d = result.Data!;
        return Ok(new
        {
            d.Id, d.StrategyId, traderId,
            scope = ResolveScope(d.Pairs, Guid.Empty),
            d.Pairs, d.Timeframe, d.Status
        });
    }

    [HttpDelete("{id:guid}")]
    [RequireMfa]
    public async Task<IActionResult> Delete(Guid traderId, Guid id, CancellationToken ct)
    {
        var result = await deleteBinding.ExecuteAsync(new DeleteBindingCommand(id, traderId, UserId), ct);
        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        return NoContent();
    }

    [HttpPost("{id:guid}/toggle")]
    [RequireMfa]
    public async Task<IActionResult> Toggle(Guid traderId, Guid id, [FromBody] ToggleBindingRequest request, CancellationToken ct)
    {
        Result<BindingDto> result;
        if (request.Enable)
        {
            result = await activateBinding.ExecuteAsync(new ActivateBindingCommand(id, traderId, UserId), ct);
        }
        else
        {
            result = await deactivateBinding.ExecuteAsync(new DeactivateBindingCommand(id, traderId, UserId), ct);
        }

        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                409 => Conflict(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        // 通知 Worker 刷新 K 线订阅（仅 Redis 可用时生效）
        await commandPublisher.PublishAsync(WorkerCommandTypes.RefreshSubscriptions, ct: ct);

        return Ok(new { result.Data!.Id, Status = result.Data.Status, UpdatedAt = Fmt(DateTime.UtcNow) });
    }

    public record CreateBindingRequest(Guid StrategyId, Guid? ExchangeId, string? Pairs = null, string? Timeframe = null, string? Name = null);
    public record UpdateBindingRequest(string? Pairs = null, string? Timeframe = null, string? Name = null);
    public record ToggleBindingRequest(bool Enable);
}
