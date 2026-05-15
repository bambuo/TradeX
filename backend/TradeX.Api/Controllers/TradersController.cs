using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeX.Api.Filters;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Core.Enums;
using TradeX.Infrastructure.Data;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TradersController(
    ITraderRepository traderRepo,
    IStrategyBindingRepository bindingRepo,
    TradeXDbContext dbContext) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var traders = await traderRepo.GetByUserIdAsync(UserId, ct);
        var result = traders.Select(t => new
        {
            t.Id, t.Name, t.Status, t.AvatarColor, t.AvatarUrl, t.Style, t.CreatedAt, t.UpdatedAt
        });
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(id, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        return Ok(new
        {
            trader.Id, trader.Name, trader.Status, trader.CreatedAt, trader.UpdatedAt
        });
    }

    [HttpPost]
    [RequireMfa]
    public async Task<IActionResult> Create([FromBody] CreateTraderRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { message = "名称不能为空" });

        var isUnique = await traderRepo.IsNameUniqueAsync(UserId, request.Name, null, ct);
        if (!isUnique)
            return Conflict(new { message = "交易员名称已存在" });

        var trader = new Trader
        {
            UserId = UserId,
            Name = request.Name
        };

        await traderRepo.AddAsync(trader, ct);

        return CreatedAtAction(nameof(GetById), new { id = trader.Id }, new
        {
            trader.Id, trader.Name, trader.Status, trader.CreatedAt
        });
    }

    [HttpPut("{id:guid}")]
    [RequireMfa]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTraderRequest request, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(id, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            var isUnique = await traderRepo.IsNameUniqueAsync(UserId, request.Name, id, ct);
            if (!isUnique)
                return Conflict(new { message = "交易员名称已存在" });
            trader.Name = request.Name;
        }

        if (request.Status.HasValue)
        {
            if (request.Status.Value == TradeX.Core.Enums.TraderStatus.Disabled
                && trader.Status == TradeX.Core.Enums.TraderStatus.Active)
            {
                var activeBindings = await bindingRepo.GetByTraderIdAsync(trader.Id, ct);
                foreach (var binding in activeBindings.Where(d => d.Status == BindingStatus.Active))
                {
                    binding.Status = BindingStatus.Disabled;
                    await bindingRepo.UpdateAsync(binding, ct);
                }
            }

            trader.Status = request.Status.Value;
        }

        if (request.AvatarColor is not null)
            trader.AvatarColor = request.AvatarColor;

        if (request.Style is not null)
            trader.Style = request.Style;

        await traderRepo.UpdateAsync(trader, ct);

        return Ok(new { trader.Id, trader.Name, trader.Status, trader.UpdatedAt });
    }

    [HttpDelete("{id:guid}")]
    [RequireMfa]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(id, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var activeBindings = await bindingRepo.GetByTraderIdAsync(trader.Id, ct);
        if (activeBindings.Any(d => d.Status == BindingStatus.Active))
            return Conflict(new { code = "TRADER_HAS_ACTIVE_STRATEGIES", message = "交易员存在活跃策略，无法删除，请先禁用所有策略" });

        await traderRepo.DeleteAsync(trader, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/avatar")]
    public async Task<IActionResult> UploadAvatar(Guid id, IFormFile file, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(id, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "请选择图片文件" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".jpg" and not ".jpeg" and not ".png" and not ".webp" and not ".gif")
            return BadRequest(new { message = "仅支持 JPG/PNG/WebP/GIF 格式" });

        var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
        Directory.CreateDirectory(dir);

        var fileName = $"{id:N}{ext}";
        var filePath = Path.Combine(dir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream, ct);

        trader.AvatarUrl = $"/uploads/avatars/{fileName}";
        await traderRepo.UpdateAsync(trader, ct);

        return Ok(new { avatarUrl = trader.AvatarUrl });
    }

    [HttpGet("{id:guid}/stats")]
    public async Task<IActionResult> GetStats(Guid id, CancellationToken ct)
    {
        var trader = await traderRepo.GetByIdAsync(id, ct);
        if (trader is null || trader.UserId != UserId)
            return NotFound(new { message = "交易员不存在" });

        var filledOrders = await dbContext.Orders
            .Where(o => o.TraderId == id && o.Status == OrderStatus.Filled && o.QuoteQuantity > 0)
            .ToListAsync(ct);

        var totalTrades = filledOrders.Count;
        var wins = filledOrders.Count(o => o.Side == OrderSide.Sell && (o.FilledQuantity * (o.Price ?? 0)) > o.QuoteQuantity);
        var winRate = totalTrades > 0 ? Math.Round((decimal)wins / totalTrades * 100, 1) : 0m;

        var profitableTrades = filledOrders
            .Where(o => o.Side == OrderSide.Sell && (o.FilledQuantity * (o.Price ?? 0)) > o.QuoteQuantity)
            .Select(o => Math.Abs((o.FilledQuantity * (o.Price ?? 0)) - o.QuoteQuantity))
            .ToList();

        var losingTrades = filledOrders
            .Where(o => o.Side == OrderSide.Sell && (o.FilledQuantity * (o.Price ?? 0)) <= o.QuoteQuantity)
            .Select(o => Math.Abs((o.FilledQuantity * (o.Price ?? 0)) - o.QuoteQuantity))
            .ToList();

        var avgWin = profitableTrades.Count > 0 ? profitableTrades.Average() : 0m;
        var avgLoss = losingTrades.Count > 0 ? losingTrades.Average() : 0m;
        var profitLossRatio = avgLoss > 0 ? Math.Round(avgWin / avgLoss, 2) : 0m;

        var sharpeRatio = totalTrades > 1 ? (decimal)Math.Round((double)winRate / 100 * Math.Sqrt(365), 2) : 0m;

        return Ok(new
        {
            totalTrades,
            winRate,
            profitLossRatio,
            sharpeRatio
        });
    }

    public record CreateTraderRequest(string Name, string? AvatarColor = null, string? Style = null);

    public record UpdateTraderRequest(string? Name = null, TradeX.Core.Enums.TraderStatus? Status = null, string? AvatarColor = null, string? Style = null);
}
