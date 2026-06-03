using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;

namespace TradeX.Api.Controllers;

[ApiController]
public class HealthController(IHealthCheckService health) : ControllerBase
{
    [HttpGet("/health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var result = await health.CheckAsync(ct);
        return Ok(result);
    }
}
