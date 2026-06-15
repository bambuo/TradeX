using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;
using TradeX.Application.System;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/system")]
public class SystemController(
    IUseCase<EmergencyStopCommand, Result<EmergencyStopResultDto>> emergencyStop) : ControllerBase
{
    [HttpPost("emergency-stop")]
    public async Task<IActionResult> EmergencyStop(CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var cmd = new EmergencyStopCommand(userId is not null ? Guid.Parse(userId) : Guid.Empty);
        var result = await emergencyStop.ExecuteAsync(cmd, ct);

        if (!result.Success)
            return StatusCode(500, new { code = "EMERGENCY_STOP_FAILED", message = result.Error });

        var dto = result.Data!;
        return Ok(new
        {
            success = dto.Success,
            disabledExchanges = dto.DisabledExchanges,
            cancelledOrders = dto.CancelledOrders,
            message = dto.Message
        });
    }
}
