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
public class StrategyBindingsController(
    ITraderRepository traderRepo,
    IStrategyRepository strategyRepo,
    IStrategyBindingRepository bindingRepo,
    ILogger<StrategyBindingsController> logger) : ControllerBase
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
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var bindings = await bindingRepo.GetByTraderIdAsync(traderId, ct);
        return Ok(bindings.Select(d => new
        {
            d.Id, d.StrategyId, d.TraderId, d.ExchangeId, d.Pairs,
            d.Timeframe, d.Status, scope = ResolveScope(d.Pairs, d.ExchangeId),
            CreatedAt = Fmt(d.CreatedAt), UpdatedAt = Fmt(d.UpdatedAt)
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid traderId, Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var binding = await bindingRepo.GetByIdAsync(id, ct);
        if (binding is null || binding.TraderId != traderId)
            return NotFound(new { message = "绑定策略不存在" });

        return Ok(new
        {
            binding.Id, binding.StrategyId, binding.Name, binding.TraderId, binding.ExchangeId,
            binding.Pairs, binding.Timeframe, binding.Status,
            scope = ResolveScope(binding.Pairs, binding.ExchangeId),
            CreatedAt = Fmt(binding.CreatedAt), UpdatedAt = Fmt(binding.UpdatedAt)
         });
     }

    [HttpPost]
    public async Task<IActionResult> Create(Guid traderId, [FromBody] CreateBindingRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var strategy = await strategyRepo.GetByIdAsync(request.StrategyId, ct);

        var exchangeId = request.ExchangeId ?? Guid.Empty;
        var pairs = request.Pairs ?? "[]";
        var scope = ResolveScope(pairs, exchangeId);

        var binding = new StrategyBinding
        {
            TraderId = traderId,
            StrategyId = request.StrategyId,
            Name = strategy?.Name ?? "未知策略",
            ExchangeId = exchangeId,
            Pairs = pairs,
            Timeframe = request.Timeframe ?? "15m",
            CreatedBy = UserId
        };

        await bindingRepo.AddAsync(binding, ct);

        return CreatedAtAction(nameof(GetById), new { traderId, id = binding.Id }, new
        {
            binding.Id, binding.StrategyId, binding.TraderId, binding.ExchangeId,
            binding.Pairs, binding.Timeframe, binding.Status, scope,
            CreatedAt = Fmt(binding.CreatedAt)
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid traderId, Guid id, [FromBody] UpdateBindingRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var binding = await bindingRepo.GetByIdAsync(id, ct);
        if (binding is null || binding.TraderId != traderId)
            return NotFound(new { message = "绑定策略不存在" });

        if (binding.Status == BindingStatus.Active)
            return BadRequest(new { message = "活跃策略不可编辑，请先禁用" });

        if (request.Pairs is not null)
            binding.Pairs = request.Pairs;
        if (request.Timeframe is not null)
            binding.Timeframe = request.Timeframe;

        await bindingRepo.UpdateAsync(binding, ct);

        return Ok(new
        {
            binding.Id, binding.StrategyId, binding.TraderId, binding.ExchangeId,
            binding.Pairs, binding.Timeframe, binding.Status,
            scope = ResolveScope(binding.Pairs, binding.ExchangeId),
            UpdatedAt = Fmt(binding.UpdatedAt)
        });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid traderId, Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var binding = await bindingRepo.GetByIdAsync(id, ct);
        if (binding is null || binding.TraderId != traderId)
            return NotFound(new { message = "绑定策略不存在" });

        if (binding.Status == BindingStatus.Active)
            return BadRequest(new { message = "活跃策略不可删除，请先禁用" });

        await bindingRepo.DeleteAsync(binding, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> Toggle(Guid traderId, Guid id, [FromBody] ToggleBindingRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(traderId, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var binding = await bindingRepo.GetByIdAsync(id, ct);
        if (binding is null || binding.TraderId != traderId)
            return NotFound(new { message = "绑定策略不存在" });

        if (request.Enable)
        {
            var pairs = ParsePairs(binding.Pairs);
            foreach (var pair in pairs)
            {
                var hasConflict = await bindingRepo.ExistsActiveAsync(traderId, binding.ExchangeId, pair, id, ct);
                if (hasConflict)
                    return Conflict(new { message = $"交易对 {pair} 上已有活跃策略" });
            }

            binding.Status = BindingStatus.Active;
        }
        else
        {
            binding.Status = BindingStatus.Disabled;
        }

        await bindingRepo.UpdateAsync(binding, ct);

        return Ok(new { binding.Id, binding.Status, UpdatedAt = Fmt(binding.UpdatedAt) });
    }

    public record CreateBindingRequest(Guid StrategyId, Guid? ExchangeId, string? Pairs = null, string? Timeframe = null);
    public record UpdateBindingRequest(string? Pairs = null, string? Timeframe = null);
    public record ToggleBindingRequest(bool Enable);

    private string[] ParsePairs(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw == "[]") return [];
        try
        {
            var parsed = System.Text.Json.JsonSerializer.Deserialize<string[]>(raw);
            return parsed ?? [];
        }
        catch
        {
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
