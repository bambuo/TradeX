using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Core.Models;
using TradeX.Trading;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/traders/{traderId:guid}/strategies/{strategyId:guid}/backtests")]
public class BacktestingController(
    IBacktestService backtestService,
    TaskAnalysisStore analysisStore,
    ILogger<BacktestingController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> StartBacktest(
        Guid strategyId,
        [FromQuery] Guid exchangeId,
        [FromQuery] string symbolId,
        [FromQuery] string timeframe,
        [FromQuery] DateTime startUtc,
        [FromQuery] DateTime endUtc,
        [FromQuery] decimal initialCapital = 1000m,
        CancellationToken ct = default)
    {
        try
        {
            var task = await backtestService.StartBacktestAsync(strategyId, exchangeId, symbolId, timeframe, startUtc, endUtc, initialCapital, ct);
            return Ok(new
            {
                taskId = task.Id,
                status = task.Status.ToString(),
                createdAt = task.CreatedAtUtc,
                strategyName = task.StrategyName,
                symbolId = task.SymbolId,
                timeframe = task.Timeframe
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
            t.Id, t.StrategyId, t.StrategyName, t.SymbolId, t.Timeframe, t.InitialCapital,
            status = t.Status.ToString(),
            phase = t.Phase?.ToString(),
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
            phase = task.Phase?.ToString(),
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
            analysisCount = result.AnalysisJson is not null
                ? JsonSerializer.Deserialize<object[]>(result.AnalysisJson)?.Length ?? 0
                : 0,
            trades = JsonSerializer.Deserialize<object>(result.DetailJson)
        });
    }

    [HttpGet("tasks/{taskId:guid}/analysis")]
    public async Task<IActionResult> GetAnalysis(Guid taskId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 10000);

        // Check running store first
        var runningList = analysisStore.Get(taskId);
        if (runningList is not null)
        {
            var total = runningList.Count;
            var items = runningList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new
                {
                    a.Index, a.Timestamp, a.Open, a.High, a.Low, a.Close, a.Volume,
                    indicators = a.IndicatorValues,
                    entry = a.EntryConditionResult,
                    exit = a.ExitConditionResult,
                    inPosition = a.InPosition,
                    a.Action
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

        // Fallback to completed result from DB
        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task is null)
            return NotFound(new { error = "回测任务不存在" });

        if (task.Status != BacktestTaskStatus.Completed)
            return NotFound(new { error = "没有运行中的分析数据" });

        var result = await backtestService.GetResultAsync(taskId, ct);
        if (result?.AnalysisJson is null)
            return NotFound(new { error = "没有可用的分析数据" });

        var allData = JsonSerializer.Deserialize<List<JsonElement>>(result.AnalysisJson);
        if (allData is null || allData.Count == 0)
            return NotFound(new { error = "没有可用的分析数据" });

        var totalCount = allData.Count;
        var pageItems = allData
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            total = totalCount,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)totalCount / pageSize),
            items = pageItems
        });
    }

    [HttpGet("tasks/{taskId:guid}/analysis/stream")]
    public async Task GetAnalysisStream(Guid taskId,
        [FromQuery] int speed = 1,
        CancellationToken ct = default)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["Connection"] = "keep-alive";
        Response.Headers["X-Accel-Buffering"] = "no";

        var ss = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        speed = Math.Clamp(speed, 1, 50);

        // Check if running task — live stream from channel
        var runningList = analysisStore.Get(taskId);
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

        // Completed task — replay from DB with speed control
        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task?.Status != BacktestTaskStatus.Completed)
        {
            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        var result = await backtestService.GetResultAsync(taskId, ct);
        if (result?.AnalysisJson is null)
        {
            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        var allData = JsonSerializer.Deserialize<List<BacktestCandleAnalysis>>(result.AnalysisJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (allData is null || allData.Count == 0)
        {
            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        var delayMs = 300 / speed;
        var total = allData.Count;

        // Send total count first
        var metaPayload = JsonSerializer.Serialize(new { type = "meta", total }, ss);
        await Response.WriteAsync($"data: {metaPayload}\n\n", ct);
        await Response.Body.FlushAsync(ct);

        for (var i = 0; i < allData.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            await WriteItemAsync(Response, allData[i], ss, ct);
            if (delayMs > 0) await Task.Delay(delayMs, ct);
        }

        await WriteCompleteAsync(Response, ss, ct);
    }

    private static object FormatItem(BacktestCandleAnalysis a) => new
    {
        a.Index, a.Timestamp, a.Open, a.High, a.Low, a.Close, a.Volume,
        indicators = a.IndicatorValues,
        entry = a.EntryConditionResult,
        exit = a.ExitConditionResult,
        inPosition = a.InPosition,
        a.Action
    };

    private static async Task WriteItemAsync(HttpResponse response, BacktestCandleAnalysis a, JsonSerializerOptions ss, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            type = "item",
            a.Index, a.Timestamp, a.Open, a.High, a.Low, a.Close, a.Volume,
            indicators = a.IndicatorValues,
            entry = a.EntryConditionResult,
            exit = a.ExitConditionResult,
            inPosition = a.InPosition,
            a.Action
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
