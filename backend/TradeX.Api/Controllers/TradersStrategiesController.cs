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
public class TradersStrategiesController(
    ITraderRepository traderRepo,
    IStrategyRepository strategyRepo,
    IStrategyDeploymentRepository deploymentRepo) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    private static string ResolveScope(string symbolIds, Guid exchangeId)
    {
        var hasSymbols = !string.IsNullOrWhiteSpace(symbolIds)
            && symbolIds != "[]"
            && symbolIds.Replace("\"", "").Replace("[", "").Replace("]", "").Trim().Length > 0;
        if (hasSymbols) return "Symbol";
        if (exchangeId != Guid.Empty) return "Exchange";
        return "Trader";
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(Guid traderId, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var deployments = await deploymentRepo.GetByTraderIdAsync(traderId, ct);
        return Ok(deployments.Select(d => new
        {
            d.Id, d.StrategyId, d.TraderId, d.ExchangeId, d.SymbolIds,
            d.Timeframe, d.Status, scope = ResolveScope(d.SymbolIds, d.ExchangeId),
            d.CreatedAtUtc, d.UpdatedAtUtc
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid traderId, Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var deployment = await deploymentRepo.GetByIdAsync(id, ct);
        if (deployment is null || deployment.TraderId != traderId)
            return NotFound(new { message = "策略部署不存在" });

        return Ok(new
        {
            deployment.Id, deployment.StrategyId, deployment.TraderId, deployment.ExchangeId,
            deployment.SymbolIds, deployment.Timeframe, deployment.Status,
            scope = ResolveScope(deployment.SymbolIds, deployment.ExchangeId),
            deployment.CreatedAtUtc, deployment.UpdatedAtUtc
        });
    }

    [HttpPost]
    public async Task<IActionResult> Create(Guid traderId, [FromBody] CreateDeploymentRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var strategy = await strategyRepo.GetByIdAsync(request.StrategyId, ct);

        var exchangeId = request.ExchangeId ?? Guid.Empty;
        var symbolIds = request.SymbolIds ?? "[]";
        var scope = ResolveScope(symbolIds, exchangeId);

        var deployment = new StrategyDeployment
        {
            TraderId = traderId,
            StrategyId = request.StrategyId,
            Name = strategy?.Name ?? "未知策略",
            ExchangeId = exchangeId,
            SymbolIds = symbolIds,
            Timeframe = request.Timeframe ?? "15m",
            CreatedBy = UserId
        };

        await deploymentRepo.AddAsync(deployment, ct);

        return CreatedAtAction(nameof(GetById), new { traderId, id = deployment.Id }, new
        {
            deployment.Id, deployment.StrategyId, deployment.TraderId, deployment.ExchangeId,
            deployment.SymbolIds, deployment.Timeframe, deployment.Status, scope,
            deployment.CreatedAtUtc
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid traderId, Guid id, [FromBody] UpdateDeploymentRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var deployment = await deploymentRepo.GetByIdAsync(id, ct);
        if (deployment is null || deployment.TraderId != traderId)
            return NotFound(new { message = "策略部署不存在" });

        if (deployment.Status == StrategyStatus.Active)
            return BadRequest(new { message = "活跃策略不可编辑，请先禁用" });

        if (request.SymbolIds is not null)
            deployment.SymbolIds = request.SymbolIds;
        if (request.Timeframe is not null)
            deployment.Timeframe = request.Timeframe;

        await deploymentRepo.UpdateAsync(deployment, ct);

        return Ok(new
        {
            deployment.Id, deployment.StrategyId, deployment.TraderId, deployment.ExchangeId,
            deployment.SymbolIds, deployment.Timeframe, deployment.Status,
            scope = ResolveScope(deployment.SymbolIds, deployment.ExchangeId),
            deployment.UpdatedAtUtc
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid traderId, Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var deployment = await deploymentRepo.GetByIdAsync(id, ct);
        if (deployment is null || deployment.TraderId != traderId)
            return NotFound(new { message = "策略部署不存在" });

        if (deployment.Status == StrategyStatus.Active)
            return BadRequest(new { message = "活跃策略不可删除，请先禁用" });

        await deploymentRepo.DeleteAsync(deployment, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid traderId, Guid id, [FromBody] ToggleDeploymentRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var deployment = await deploymentRepo.GetByIdAsync(id, ct);
        if (deployment is null || deployment.TraderId != traderId)
            return NotFound(new { message = "策略部署不存在" });

        if (request.Enable)
        {
            if (deployment.Status == StrategyStatus.Draft)
                return BadRequest(new { message = "草稿策略必须先通过回测才能启用" });

            var symbolIds = deployment.SymbolIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var symbolId in symbolIds)
            {
                var hasConflict = await deploymentRepo.ExistsActiveAsync(traderId, deployment.ExchangeId, symbolId, id, ct);
                if (hasConflict)
                    return Conflict(new { message = $"交易对 {symbolId} 上已有活跃策略" });
            }

            deployment.Status = StrategyStatus.Active;
        }
        else
        {
            deployment.Status = StrategyStatus.Disabled;
        }

        await deploymentRepo.UpdateAsync(deployment, ct);

        return Ok(new { deployment.Id, deployment.Status, deployment.UpdatedAtUtc });
    }

    public record CreateDeploymentRequest(Guid StrategyId, Guid? ExchangeId, string? SymbolIds = null, string? Timeframe = null);
    public record UpdateDeploymentRequest(string? SymbolIds = null, string? Timeframe = null);
    public record ToggleDeploymentRequest(bool Enable);
}
