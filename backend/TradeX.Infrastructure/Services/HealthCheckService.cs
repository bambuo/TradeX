using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TradeX.Core.Interfaces;
using TradeX.Infrastructure.Data;

namespace TradeX.Infrastructure.Services;

/// <summary>
/// 健康检查服务实现。通过直接检查 DbContext 数据库连接来判断服务状态。
/// </summary>
public sealed class HealthCheckService(
    TradeXDbContext db,
    ILogger<HealthCheckService> logger) : IHealthCheckService
{
    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
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
        return new HealthCheckResult(status, dbConnected ? "Connected" : "Disconnected", DateTime.UtcNow);
    }
}
