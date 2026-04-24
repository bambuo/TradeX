using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Trading;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/traders/{traderId:guid}/strategies/{strategyId:guid}/backtests")]
public class BacktestingController(
    IBacktestService backtestService) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> StartBacktest(
        Guid strategyId,
        [FromQuery] DateTime startUtc,
        [FromQuery] DateTime endUtc,
        CancellationToken ct)
    {
        try
        {
            var task = await backtestService.StartBacktestAsync(strategyId, startUtc, endUtc, ct);
            return Ok(new
            {
                taskId = task.Id,
                status = task.Status.ToString(),
                createdAt = task.CreatedAtUtc
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks(Guid strategyId, CancellationToken ct)
    {
        var tasks = await backtestService.GetTasksByStrategyAsync(strategyId, ct);
        return Ok(tasks.Select(t => new
        {
            t.Id, t.StrategyId,
            status = t.Status.ToString(),
            t.StartAtUtc, t.EndAtUtc,
            t.CreatedAtUtc, t.CompletedAtUtc
        }));
    }

    [HttpGet("tasks/{taskId:guid}")]
    public async Task<IActionResult> GetTask(Guid taskId, CancellationToken ct)
    {
        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task is null) return NotFound(new { error = "回测任务不存在" });
        return Ok(new
        {
            task.Id, task.StrategyId,
            status = task.Status.ToString(),
            task.StartAtUtc, task.EndAtUtc,
            task.CreatedAtUtc, task.CompletedAtUtc
        });
    }

    [HttpGet("tasks/{taskId:guid}/result")]
    public async Task<IActionResult> GetResult(Guid taskId, CancellationToken ct)
    {
        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task is null) return NotFound(new { error = "回测任务不存在" });

        if (task.Status != Core.Models.BacktestTaskStatus.Completed)
            return BadRequest(new { error = "回测尚未完成", status = task.Status.ToString() });

        var result = await backtestService.GetResultAsync(taskId, ct);
        if (result is null) return NotFound(new { error = "回测结果不存在" });

        return Ok(new
        {
            result.TotalReturnPercent,
            result.AnnualizedReturnPercent,
            result.MaxDrawdownPercent,
            result.WinRate,
            result.TotalTrades,
            result.SharpeRatio,
            result.ProfitLossRatio,
            trades = System.Text.Json.JsonSerializer.Deserialize<object>(result.DetailJson)
        });
    }
}
