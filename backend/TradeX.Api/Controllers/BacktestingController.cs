using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Backtesting;
using TradeX.Application.Common;
using TradeX.Core.Models;
using TradeX.Trading;
using TradeX.Trading.Backtest;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/backtests")]
public class BacktestingController(
    IBacktestService backtestService,
    IUseCase<GetBacktestTasksQuery, Result<List<BacktestTaskDto>>> getBacktestTasks,
    IUseCase<GetBacktestTaskByIdQuery, Result<BacktestTaskDto>> getBacktestTaskById,
    IUseCase<CancelBacktestCommand, Result> cancelBacktest,
    IUseCase<GetBacktestAnalysisPageQuery, Result<BacktestAnalysisPageDto>> getBacktestAnalysisPage,
    IUseCase<GetBacktestAnalysisAllQuery, Result<BacktestKlineAnalysis[]>> getBacktestAnalysisAll,
    IUseCase<GetBacktestAnalysisCountQuery, Result<int>> getBacktestAnalysisCount,
    IBacktestCancellationNotifier cancellationNotifier) : ControllerBase
{
    public record StartBacktestRequest(
        Guid StrategyId,
        Guid ExchangeId,
        string Pair,
        string Timeframe,
        DateTime StartAt,
        DateTime EndAt,
        decimal InitialCapital,
        decimal? PositionSize = null);

    [HttpPost]
    public async Task<IActionResult> StartBacktest(
        [FromBody] StartBacktestRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var task = await backtestService.StartBacktestAsync(request.StrategyId, request.ExchangeId, request.Pair, request.Timeframe, request.StartAt, request.EndAt, request.InitialCapital, request.PositionSize, ct);
            return Ok(new
            {
                taskId = task.Id,
                status = task.Status.ToString(),
                createdAt = task.CreatedAt,
                strategyName = task.StrategyName,
                pair = task.Pair,
                timeframe = task.Timeframe
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks([FromQuery] Guid? strategyId, CancellationToken ct)
    {
        var query = new GetBacktestTasksQuery(strategyId, Guid.Empty);
        var result = await getBacktestTasks.ExecuteAsync(query, ct);
        if (!result.Success)
            return BadRequest(new { error = result.Error });

        return Ok(result.Data!.Select(t => new
        {
            t.Id, t.StrategyName, t.Pair,
            status = t.Status,
            phase = t.Phase,
            t.InitialCapital, t.CreatedAt, t.CompletedAt
        }));
    }

    [HttpGet("tasks/{taskId:guid}")]
    public async Task<IActionResult> GetTask(Guid taskId, CancellationToken ct)
    {
        var query = new GetBacktestTaskByIdQuery(taskId, Guid.Empty);
        var result = await getBacktestTaskById.ExecuteAsync(query, ct);
        if (!result.Success)
            return NotFound(new { error = "回测任务不存在" });

        var t = result.Data!;
        return Ok(new
        {
            t.Id, strategyId = t.Id,
            status = t.Status,
            phase = t.Phase,
            createdAt = t.CreatedAt,
            completedAt = t.CompletedAt
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

        var countResult = await getBacktestAnalysisCount.ExecuteAsync(new GetBacktestAnalysisCountQuery(taskId), ct);

        return Ok(new
        {
            result.TotalReturnPercent,
            result.AnnualizedReturnPercent,
            result.MaxDrawdownPercent,
            result.WinRate,
            result.TotalTrades,
            result.SharpeRatio,
            result.ProfitLossRatio,
            analysisCount = countResult.Success ? countResult.Data : 0,
            trades = JsonSerializer.Deserialize<object>(result.Details)
        });
    }

    [HttpDelete("tasks/{taskId:guid}")]
    public async Task<IActionResult> CancelBacktest(Guid taskId, CancellationToken ct)
    {
        var query = new CancelBacktestCommand(taskId, Guid.Empty);
        var result = await cancelBacktest.ExecuteAsync(query, ct);
        if (!result.Success)
            return BadRequest(new { error = result.Error ?? "任务不存在或已处于终态，无法取消" });

        // 事件驱动：发布取消事件到 Redis Stream，Worker 端 BacktestCancellationConsumer 立即响应
        await cancellationNotifier.NotifyCancellationAsync(taskId, ct);

        return Ok(new { taskId, status = "Cancelled" });
    }

    [HttpGet("tasks/{taskId:guid}/analysis")]
    public async Task<IActionResult> GetAnalysis(Guid taskId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? action = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 10000);

        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task is null)
            return NotFound(new { error = "回测任务不存在" });

        if (task.Status != Core.Models.BacktestTaskStatus.Completed)
            return Accepted(new
            {
                taskId,
                total = 0,
                page,
                pageSize,
                totalPages = 0,
                items = (object[])[],
                status = task.Status.ToString(),
                phase = task.Phase?.ToString(),
                message = "回测分析数据将在任务完成后从数据库读取"
            });

        var result = await getBacktestAnalysisPage.ExecuteAsync(
            new GetBacktestAnalysisPageQuery(taskId, page, pageSize, action), ct);

        if (!result.Success)
            return BadRequest(new { error = result.Error });

        var data = result.Data!;
        return Ok(new
        {
            data.Total,
            data.Page,
            data.PageSize,
            data.TotalPages,
            items = data.Items.Select(FormatItem).ToList()
        });
    }

    [HttpGet("tasks/{taskId:guid}/analysis/stream")]
    public async Task GetAnalysisStream(Guid taskId,
        [FromQuery] int speed = 1,
        CancellationToken ct = default)
    {
        try
        {
            await GetAnalysisStreamInner(taskId, speed, ct);
        }
        catch (OperationCanceledException)
        {
            // client disconnected — exit cleanly, no 500
        }
    }

    private async Task GetAnalysisStreamInner(Guid taskId,
        int speed,
        CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var ss = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        speed = Math.Clamp(speed, 1, 50);

        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task?.Status != Core.Models.BacktestTaskStatus.Completed)
        {
            var statusPayload = JsonSerializer.Serialize(new
            {
                type = "status",
                status = task?.Status.ToString() ?? "NotFound",
                phase = task?.Phase?.ToString()
            }, ss);
            await Response.WriteAsync($"data: {statusPayload}\n\n", ct);
            await Response.Body.FlushAsync(ct);
            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        var allResult = await getBacktestAnalysisAll.ExecuteAsync(new GetBacktestAnalysisAllQuery(taskId), ct);
        var allData = allResult.Success ? allResult.Data! : [];

        if (allData.Length == 0)
        {
            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        var delayMs = 300 / speed;
        var total = allData.Length;

        var metaPayload = JsonSerializer.Serialize(new { type = "meta", total }, ss);
        await Response.WriteAsync($"data: {metaPayload}\n\n", ct);
        await Response.Body.FlushAsync(ct);

        for (var i = 0; i < allData.Length; i++)
        {
            if (ct.IsCancellationRequested) break;
            await WriteItemAsync(Response, allData[i], ss, ct);
            if (delayMs > 0) await Task.Delay(delayMs, ct);
        }

        await WriteCompleteAsync(Response, ss, ct);
    }

    private static object FormatItem(BacktestKlineAnalysis a) => new
    {
        a.Index, a.Timestamp, a.Open, a.High, a.Low, a.Close, a.Volume,
        indicators = a.IndicatorValues,
        entry = a.EntryConditionResult,
        exit = a.ExitConditionResult,
        inPosition = a.InPosition,
        a.Action,
        a.AvgEntryPrice,
        a.PositionQuantity,
        a.PositionCost,
        a.PositionValue,
        a.PositionPnl,
        a.PositionPnlPercent
    };

    private static async Task WriteItemAsync(HttpResponse response, BacktestKlineAnalysis a, JsonSerializerOptions ss, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = "item",
            a.Index, a.Timestamp, a.Open, a.High, a.Low, a.Close, a.Volume,
            indicators = a.IndicatorValues,
            entry = a.EntryConditionResult,
            exit = a.ExitConditionResult,
            inPosition = a.InPosition,
            a.Action,
            a.AvgEntryPrice,
            a.PositionQuantity,
            a.PositionCost,
            a.PositionValue,
            a.PositionPnl,
            a.PositionPnlPercent
        }, ss);
        await response.WriteAsync($"data: {payload}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }

    private static async Task WriteCompleteAsync(HttpResponse response, JsonSerializerOptions ss, CancellationToken ct)
    {
        await response.WriteAsync($"data: {JsonSerializer.Serialize(new { type = "complete" })}\n\n", ct);
        await response.Body.FlushAsync(ct);
    }
}
