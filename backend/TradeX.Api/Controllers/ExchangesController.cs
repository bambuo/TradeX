using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.Exchanges;
using TradeX.Core.Interfaces;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/exchanges")]
[Authorize]
public class ExchangesController(
    IUseCase<GetExchangesQuery, Result<List<ExchangeDto>>> getExchanges,
    IUseCase<GetExchangeByIdQuery, Result<ExchangeDto>> getExchangeById,
    IUseCase<CreateExchangeCommand, Result<ExchangeDto>> createExchange,
    IUseCase<UpdateExchangeCommand, Result<ExchangeDto>> updateExchange,
    IUseCase<DeleteExchangeCommand, Result> deleteExchange,
    IUseCase<TestExchangeCommand, Result<ExchangeTestResultDto>> testExchange,
    IUseCase<GetExchangeAssetsCommand, Result<List<ExchangeAssetDto>>> getExchangeAssets,
    IUseCase<GetExchangePairsCommand, Result<List<ExchangePairDto>>> getExchangePairs,
    IUseCase<GetExchangeOrdersQuery, Result<PagedExchangeOrderDto>> getExchangeOrders,
    IUseCase<ToggleExchangeCommand, Result> toggleExchange) : ControllerBase
{
    private Guid UserId => Guid.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken ct)
    {
        var result = await getExchanges.ExecuteAsync(new GetExchangesQuery(UserId), ct);
        return Ok(new { data = result.Data });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var result = await getExchangeById.ExecuteAsync(new GetExchangeByIdQuery(id, UserId), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });
        return Ok(result.Data);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateExchangeRequest request, CancellationToken ct)
    {
        var result = await createExchange.ExecuteAsync(
            new CreateExchangeCommand(UserId, request.Name, request.ExchangeType,
                request.ApiKey, request.SecretKey, request.Passphrase, null), ct);

        if (!result.Success)
            return result.StatusCode switch
            {
                409 => Conflict(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        return CreatedAtAction(nameof(GetAll), null, new
        {
            result.Data!.Id,
            label = result.Data.Name,
            exchangeType = result.Data.Type,
            isEnabled = result.Data.Status == "Enabled",
            createdAt = result.Data.CreatedAt,
            updatedAt = result.Data.UpdatedAt
        });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateExchangeRequest request, CancellationToken ct)
    {
        var result = await updateExchange.ExecuteAsync(
            new UpdateExchangeCommand(id, UserId, request.Name, request.ApiKey,
                request.SecretKey, request.Passphrase, null), ct);

        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                409 => Conflict(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        return Ok(new { result.Data!.Id, result.Data.Name, result.Data.UpdatedAt });
    }

    [HttpPost("{id:guid}/test")]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken ct)
    {
        var result = await testExchange.ExecuteAsync(new TestExchangeCommand(id, UserId), ct);
        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                _ => BadRequest(new { message = result.Error })
            };

        var dto = result.Data!;
        return Ok(new
        {
            connected = dto.Connected,
            error = dto.Error,
            message = dto.Message,
            permissions = dto.Permissions,
            hasWarning = dto.HasWarning
        });
    }

    [HttpGet("{id:guid}/orders")]
    public async Task<IActionResult> GetOrders(Guid id,
        [FromQuery] string type = "open",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? pair = null,
        [FromQuery] string? side = null,
        [FromQuery] string? orderType = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var result = await getExchangeOrders.ExecuteAsync(
            new GetExchangeOrdersQuery(id, UserId, type, page, pageSize, pair, side, orderType, status), ct);
        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                _ => StatusCode(502, new { message = result.Error, HttpContext.TraceIdentifier })
            };

        var dto = result.Data!;
        return Ok(new { data = dto.Data, total = dto.Total, page = dto.Page, pageSize = dto.PageSize });
    }

    [HttpPost("{id:guid}/toggle")]
    public async Task<IActionResult> ToggleStatus(Guid id, [FromBody] ToggleExchangeRequest request, CancellationToken ct)
    {
        var result = await toggleExchange.ExecuteAsync(new ToggleExchangeCommand(id, UserId, request.Enable), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });

        return Ok(new { id, isEnabled = request.Enable });
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var result = await deleteExchange.ExecuteAsync(new DeleteExchangeCommand(id, UserId), ct);
        if (!result.Success)
            return NotFound(new { message = result.Error });

        return NoContent();
    }

    [HttpGet("{id:guid}/assets")]
    public async Task<IActionResult> GetAssets(Guid id, CancellationToken ct)
    {
        var result = await getExchangeAssets.ExecuteAsync(new GetExchangeAssetsCommand(id, UserId), ct);
        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                _ => StatusCode(502, new { message = result.Error, HttpContext.TraceIdentifier })
            };

        return Ok(new { data = result.Data });
    }

    [HttpGet("{id:guid}/pairs")]
    public async Task<IActionResult> GetPairs(Guid id, CancellationToken ct, [FromQuery] string? quoteCurrency = "USDT")
    {
        var result = await getExchangePairs.ExecuteAsync(new GetExchangePairsCommand(id, UserId, quoteCurrency), ct);
        if (!result.Success)
            return result.StatusCode switch
            {
                404 => NotFound(new { message = result.Error }),
                _ => StatusCode(502, new { message = result.Error, HttpContext.TraceIdentifier })
            };

        return Ok(new { data = result.Data });
    }

    public record CreateExchangeRequest(string Name, string ExchangeType, string ApiKey, string SecretKey, string? Passphrase = null);
    public record UpdateExchangeRequest(string? Name = null, string? ApiKey = null, string? SecretKey = null, string? Passphrase = null);
    public record ToggleExchangeRequest(bool Enable);
}
