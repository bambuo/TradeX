using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Common;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet("summary")]
    [HttpGet("stats")]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var summary = await dashboard.GetSummaryAsync(ct);
        return Ok(new
        {
            traderCount = summary.TraderCount,
            strategyCount = summary.StrategyCount,
            totalBalance = summary.TotalBalance,
            totalPnl = summary.TotalPnl,
            todayOrderCount = summary.TodayOrderCount,
            totalPnlPercent = summary.TotalBalance > 0
                ? Math.Round(summary.TotalPnl / summary.TotalBalance * 100, 2)
                : 0,
            openPositionCount = summary.OpenPositionCount,
            activeStrategyCount = summary.ActiveStrategyCount,
            dailyLossPercent = 0m,
            maxDrawdownPercent = 0m,
            riskStatus = "Normal",
            exchangeStatus = summary.ExchangeStatus,
            recentTrades = summary.RecentTrades
        });
    }
}
