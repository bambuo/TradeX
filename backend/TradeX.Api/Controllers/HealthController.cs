using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradeX.Core.Interfaces;
using TradeX.Infrastructure.Data;

namespace TradeX.Api.Controllers;

[ApiController]
public class HealthController(TradeXDbContext db, IIoTDbService iotdb) : ControllerBase
{
    [HttpGet("/health")]
    public async Task<IActionResult> GetHealth(CancellationToken ct)
    {
        var dbConnected = false;
        try
        {
            dbConnected = await db.Database.CanConnectAsync(ct);
        }
        catch
        {
            // ignore
        }

        var iotdbConnected = await iotdb.HealthCheckAsync(ct);

        var status = dbConnected ? "Ok" : "Degraded";

        return Ok(new
        {
            status,
            database = dbConnected ? "Connected" : "Disconnected",
            iotdb = iotdbConnected ? "Connected" : "Disconnected",
            timestamp = DateTime.UtcNow
        });
    }
}
