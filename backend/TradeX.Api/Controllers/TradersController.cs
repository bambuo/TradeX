using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Api.Filters;
using TradeX.Application.Common;
using TradeX.Application.Traders;
using TradeX.Application.Traders.DTOs;
using TradeX.Core.ErrorCodes;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class TradersController(
    IUseCase<GetTradersQuery, Result<List<TraderDto>>> getTraders,
    IUseCase<CreateTraderCommand, Result<TraderDto>> createTrader,
    IUseCase<GetTraderStatsQuery, Result<TraderStatsDto>> getTraderStats,
    IUseCase<GetTraderByIdQuery, Result<TraderDetailDto>> getTraderById,
    IUseCase<UpdateTraderCommand, Result<TraderDetailDto>> updateTrader,
    IUseCase<DeleteTraderCommand, Result> deleteTrader) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await getTraders.ExecuteAsync(new GetTradersQuery(UserId), ct);
        return Ok(result.Data);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await getTraderById.ExecuteAsync(new GetTraderByIdQuery(id, UserId), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    [RequireMfa]
    public async Task<IActionResult> Create([FromBody] CreateTraderRequest request, CancellationToken ct)
    {
        var result = await createTrader.ExecuteAsync(
            new CreateTraderCommand(UserId, request.Name, request.AvatarColor, request.Style), ct);

        if (!result.Success)
            return result.StatusCode switch
            {
                409 => Conflict(ApiResponse.Error(BusinessErrorCode.Conflict, result.Error!)),
                _ => BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, result.Error!))
            };

        return CreatedAtAction(nameof(GetById), new { id = result.Data!.Id }, ApiResponse.Ok(result.Data));
    }

    [HttpPut("{id:guid}")]
    [RequireMfa]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateTraderRequest request, CancellationToken ct)
    {
        var result = await updateTrader.ExecuteAsync(
            new UpdateTraderCommand(id, UserId, request.Name, request.Status,
                request.AvatarColor, request.Style), ct);

        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(ApiResponse.Error(BusinessErrorCode.NotFound, result.Error!)),
                409 => Conflict(ApiResponse.Error(BusinessErrorCode.Conflict, result.Error!)),
                _ => BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, result.Error!))
            };

        return Ok(ApiResponse.Ok(result.Data));
    }

    [HttpDelete("{id:guid}")]
    [RequireMfa]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await deleteTrader.ExecuteAsync(new DeleteTraderCommand(id, UserId), ct);
        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(ApiResponse.Error(BusinessErrorCode.NotFound, result.Error!)),
                _ => Conflict(ApiResponse.Error(BusinessErrorCode.Conflict, result.Error!))
            };

        return NoContent();
    }

    [HttpPost("{id:guid}/avatar")]
    public async Task<IActionResult> UploadAvatar(Guid id, IFormFile file, CancellationToken ct)
    {
        var trader = await getTraderById.ExecuteAsync(new GetTraderByIdQuery(id, UserId), ct);
        if (!trader.Success)
            return NotFound(ApiResponse.Error(BusinessErrorCode.TraderNotFound, "交易员不存在"));

        if (file is null || file.Length == 0)
            return BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, "请选择图片文件"));

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".jpg" and not ".jpeg" and not ".png" and not ".webp" and not ".gif")
            return BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, "仅支持 JPG/PNG/WebP/GIF 格式"));

        var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "avatars");
        Directory.CreateDirectory(dir);

        var fileName = $"{id:N}{ext}";
        var filePath = Path.Combine(dir, fileName);

        await using var stream = new FileStream(filePath, FileMode.Create);
        await file.CopyToAsync(stream, ct);

        var avatarResult = await updateTrader.ExecuteAsync(
            new UpdateTraderCommand(id, UserId, AvatarUrl: $"/uploads/avatars/{fileName}"), ct);

        if (!avatarResult.Success)
            return StatusCode(500, ApiResponse.Error(BusinessErrorCode.InternalError, "头像更新失败"));

        return Ok(ApiResponse.Ok(new { avatarUrl = avatarResult.Data!.AvatarUrl }));
    }

    [HttpGet("{id:guid}/stats")]
    public async Task<IActionResult> GetStats(Guid id, CancellationToken ct)
    {
        var result = await getTraderStats.ExecuteAsync(new GetTraderStatsQuery(id, UserId), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });
        return Ok(result.Data);
    }

    public record CreateTraderRequest(string Name, string? AvatarColor = null, string? Style = null);

    public record UpdateTraderRequest(string? Name = null, Core.Enums.TraderStatus? Status = null, string? AvatarColor = null, string? Style = null);
}
