using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeX.Infrastructure.Data;
using TradeX.Trading;

namespace TradeX.Api.Controllers;

[ApiController]
public class HealthController(
    TradeXDbContext db,
    ResourceMonitor resourceMonitor,
    ILogger<HealthController> logger) : ControllerBase
{
    [HttpGet("/health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var dbConnected = false;
        try
        {
            dbConnected = await db.Database.CanConnectAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "健康检查: 数据库连接失败");
        }

        var status = dbConnected ? "Ok" : "Degraded";

        return Ok(new
        {
            status,
            database = dbConnected ? "Connected" : "Disconnected",
            timestamp = DateTime.UtcNow,
            backtestScheduler = new
            {
                runningCount = resourceMonitor.RunningCount,
                allowedConcurrency = resourceMonitor.AllowedConcurrency,
                currentMemoryMb = resourceMonitor.CurrentMemoryMb,
                currentCpuPercent = resourceMonitor.CurrentCpuPercent
            }
        });
    }
}
