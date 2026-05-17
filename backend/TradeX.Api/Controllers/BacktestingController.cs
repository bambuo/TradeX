using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading;
using TradeX.Trading.Backtest;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/backtests")]
public class BacktestingController(
    IBacktestService backtestService,
    TaskAnalysisStore analysisStore,
    IBacktestTaskRepository taskRepo,
    ILogger<BacktestingController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> StartBacktest(
        [FromQuery] Guid strategyId,
        [FromQuery] Guid exchangeId,
        [FromQuery] string pair,
        [FromQuery] string timeframe,
        [FromQuery] DateTime startAt,
        [FromQuery] DateTime endAt,
        [FromQuery] decimal initialCapital,
        [FromQuery] decimal? positionSize = null,
        CancellationToken ct = default)
    {
        try
        {
            var task = await backtestService.StartBacktestAsync(strategyId, exchangeId, pair, timeframe, startAt, endAt, initialCapital, positionSize, ct);
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
        if (strategyId is null || strategyId.Value == Guid.Empty)
            return Ok(Array.Empty<object>());

        var tasks = await backtestService.GetTasksByStrategyAsync(strategyId.Value, ct);
        return Ok(tasks.Select(t => new
        {
            t.Id, t.StrategyId, t.StrategyName, t.Pair, t.Timeframe, t.InitialCapital,
            status = t.Status.ToString(),
            phase = t.Phase?.ToString(),
            t.StartAt, t.EndAt,
            t.CreatedAt, t.CompletedAt
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
            phase = task.Phase?.ToString(),
            task.StartAt, task.EndAt,
            task.CreatedAt, task.CompletedAt
        });
    }

    [HttpGet("tasks/{taskId:guid}/result")]
    public async Task<IActionResult> GetResult(Guid taskId, CancellationToken ct)
    {
        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task is null) return NotFound(new { error = "回测任务不存在" });

        if (task.Status != BacktestTaskStatus.Completed)
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
            analysisCount = await taskRepo.GetKlineAnalysesCountAsync(taskId, null, ct),
            trades = JsonSerializer.Deserialize<object>(result.Details)
        });
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

        // Check running store first
        var runningList = analysisStore.Get(taskId);
        if (runningList is not null)
        {
            var filtered = runningList.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(action) && action != "all")
                filtered = filtered.Where(a => a.Action == action);
            var total = filtered.Count();
            var items = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
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
                })
                .ToList();

            return Ok(new
            {
                total,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)total / pageSize),
                items
            });
        }

        // Completed task — read from SQLite table
        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task is null)
            return NotFound(new { error = "回测任务不存在" });

        if (task.Status != BacktestTaskStatus.Completed)
            return NotFound(new { error = "没有运行中的分析数据" });

        var dbItems = await taskRepo.GetKlineAnalysesPageAsync(taskId, page, pageSize, action, ct);
        var dbTotal = await taskRepo.GetKlineAnalysesCountAsync(taskId, action, ct);

        return Ok(new
        {
            total = dbTotal,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)dbTotal / pageSize),
            items = dbItems.Select(FormatItem).ToList()
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

        // Check if running task — live stream from channel
        var runningList = analysisStore.Get(taskId);
        if (runningList is null)
        {
            var btTask = await backtestService.GetTaskAsync(taskId, ct);
            if (btTask?.Status == BacktestTaskStatus.Running)
            {
                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                waitCts.CancelAfter(TimeSpan.FromSeconds(60));
                try
                {
                    while (!waitCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(200, waitCts.Token);
                        runningList = analysisStore.Get(taskId);
                        if (runningList is not null) break;
                        btTask = await backtestService.GetTaskAsync(taskId, ct);
                        if (btTask?.Status != BacktestTaskStatus.Running) break;
                    }
                }
                catch (OperationCanceledException) { }
            }
        }

        if (runningList is not null)
        {
            var initialCount = runningList.Count;
            if (initialCount > 0)
            {
                var batch = runningList.Select(a => FormatItem(a)).ToList();
                var batchPayload = JsonSerializer.Serialize(new { type = "batch", total = initialCount, items = batch }, ss);
                await Response.WriteAsync($"data: {batchPayload}\n\n", ct);
                await Response.Body.FlushAsync(ct);
            }

            var lastIndex = initialCount > 0 ? runningList[^1].Index : -1;
            await foreach (var a in analysisStore.SubscribeAsync(taskId, ct))
            {
                if (a.Index <= lastIndex) continue;
                lastIndex = a.Index;
                await WriteItemAsync(Response, a, ss, ct);
            }

            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        // Completed task — read from SQLite table
        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task?.Status != BacktestTaskStatus.Completed)
        {
            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        var allData = await taskRepo.GetKlineAnalysesAllAsync(taskId, ct);
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
