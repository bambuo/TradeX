using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Core.ErrorCodes;

namespace TradeX.Api.Controllers;

[ApiController]
[Route("api/v1/setup")]
public class SetupController(ISetupService setup) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var hasSuperAdmin = await setup.GetStatusAsync(ct);
        return Ok(ApiResponse.Ok(new { isInitialized = hasSuperAdmin }));
    }

    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize([FromBody] InitializeRequest request, CancellationToken ct)
    {
        var result = await setup.InitializeAsync(
            request.UserName, request.Password, request.JwtSecret, ct);

        if (result.Success)
            return NoContent();

        return result.StatusCode switch
        {
            409 => this.Conflict(result.Error!),
            _ => this.BadRequest(result.Error!)
        };
    }

    public record InitializeRequest(string UserName, string Password, string? JwtSecret);
}
