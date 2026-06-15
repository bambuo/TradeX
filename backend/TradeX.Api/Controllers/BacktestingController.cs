using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradeX.Application.Backtesting;
using TradeX.Application.Common;
using TradeX.Core.ErrorCodes;
using TradeX.Core.Models;
using TradeX.Trading;
using TradeX.Trading.Backtest;

namespace TradeX.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/backtests")]
public class BacktestingController(
    IBacktestService backtestService,
    IUseCase<GetBacktestTasksQuery, Result<List<BacktestTaskDto>>> getBacktestTasks,
    IUseCase<GetBacktestTaskByIdQuery, Result<BacktestTaskDto>> getBacktestTaskById,
    IUseCase<CancelBacktestCommand, Result> cancelBacktest,
    IUseCase<GetBacktestAnalysisPageQuery, Result<BacktestAnalysisPageDto>> getBacktestAnalysisPage,
    IUseCase<GetBacktestAnalysisAllQuery, Result<BacktestKlineAnalysis[]>> getBacktestAnalysisAll,
    IUseCase<GetBacktestAnalysisCountQuery, Result<int>> getBacktestAnalysisCount,
    IBacktestCancellationNotifier cancellationNotifier,
    TaskAnalysisStore analysisStore) : ControllerBase
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
            return Ok(ApiResponse.Ok(new
            {
                taskId = task.Id,
                status = task.Status.ToString(),
                createdAt = task.CreatedAt,
                strategyName = task.StrategyName,
                pair = task.Pair,
                timeframe = task.Timeframe
            }));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, ex.Message));
        }
    }

    [HttpGet("tasks")]
    public async Task<IActionResult> GetTasks([FromQuery] Guid? strategyId, CancellationToken ct)
    {
        var query = new GetBacktestTasksQuery(strategyId, Guid.Empty);
        var result = await getBacktestTasks.ExecuteAsync(query, ct);
        if (!result.Success)
            return BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, result.Error!));

        return Ok(ApiResponse.Ok(result.Data!.Select(t => new
        {
            t.Id, t.StrategyName, t.Pair, t.Timeframe,
            status = t.Status, phase = t.Phase,
            t.InitialCapital, t.CreatedAt, t.CompletedAt,
            startAt = t.StartAt.ToString("yyyy-MM-dd HH:mm:ss"),
            endAt = t.EndAt.ToString("yyyy-MM-dd HH:mm:ss")
        })));
    }

    [HttpGet("tasks/{taskId:guid}")]
    public async Task<IActionResult> GetTask(Guid taskId, CancellationToken ct)
    {
        var query = new GetBacktestTaskByIdQuery(taskId, Guid.Empty);
        var result = await getBacktestTaskById.ExecuteAsync(query, ct);
        if (!result.Success)
            return NotFound(ApiResponse.Error(BusinessErrorCode.NotFound, "回测任务不存在"));

        var t = result.Data!;
        return Ok(ApiResponse.Ok(new
        {
            t.Id, t.StrategyName, t.StrategyId,
            t.Pair, t.Timeframe,
            status = t.Status, phase = t.Phase,
            t.InitialCapital, t.CreatedAt, t.CompletedAt,
            startAt = t.StartAt.ToString("yyyy-MM-dd HH:mm:ss"),
            endAt = t.EndAt.ToString("yyyy-MM-dd HH:mm:ss")
        }));
    }

    [HttpGet("tasks/{taskId:guid}/result")]
    public async Task<IActionResult> GetResult(Guid taskId, CancellationToken ct)
    {
        var task = await backtestService.GetTaskAsync(taskId, ct);
        if (task is null) return NotFound(ApiResponse.Error(BusinessErrorCode.NotFound, "回测任务不存在"));

        object? resultData = null;
        if (task.Status == Core.Models.BacktestTaskStatus.Completed)
        {
            var dbResult = await backtestService.GetResultAsync(taskId, ct);
            if (dbResult is not null)
            {
                var countResult = await getBacktestAnalysisCount.ExecuteAsync(
                    new GetBacktestAnalysisCountQuery(taskId), ct);
                resultData = new
                {
                    dbResult.TotalReturnPercent,
                    dbResult.AnnualizedReturnPercent,
                    dbResult.MaxDrawdownPercent,
                    dbResult.WinRate,
                    dbResult.TotalTrades,
                    dbResult.SharpeRatio,
                    dbResult.ProfitLossRatio,
                    analysisCount = countResult.Success ? countResult.Data : 0,
                    trades = JsonSerializer.Deserialize<object>(dbResult.Details)
                };
            }
        }

        return Ok(ApiResponse.Ok(new
        {
            result = resultData,
            status = task.Status.ToString()
        }));
    }

    [HttpDelete("tasks/{taskId:guid}")]
    public async Task<IActionResult> CancelBacktest(Guid taskId, CancellationToken ct)
    {
        var query = new CancelBacktestCommand(taskId, Guid.Empty);
        var result = await cancelBacktest.ExecuteAsync(query, ct);
        if (!result.Success)
            return BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, result.Error ?? "任务不存在或已处于终态，无法取消"));

        // 事件驱动：发布取消事件到 Redis Stream，Worker 端 BacktestCancellationConsumer 立即响应
        await cancellationNotifier.NotifyCancellationAsync(taskId, ct);

        return Ok(ApiResponse.Ok(new { taskId, status = "Cancelled" }));
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
            return NotFound(ApiResponse.Error(BusinessErrorCode.NotFound, "回测任务不存在"));

        var result = await getBacktestAnalysisPage.ExecuteAsync(
            new GetBacktestAnalysisPageQuery(taskId, page, pageSize, action), ct);

        if (!result.Success)
            return BadRequest(ApiResponse.Error(BusinessErrorCode.ValidationError, result.Error!));

        var data = result.Data!;
        return Ok(ApiResponse.Ok(new
        {
            data.Total,
            data.Page,
            data.PageSize,
            data.TotalPages,
            items = data.Items.Select(FormatItem).ToList(),
            status = task.Status.ToString()
        }));
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
        var delayMs = speed > 0 ? 300 / speed : 0;

        var task = await backtestService.GetTaskAsync(taskId, ct);

        // 进行中的任务: 通过 TaskAnalysisStore Channel 实时流式推送
        if (task is not null && task.Status != Core.Models.BacktestTaskStatus.Completed && analysisStore.Exists(taskId))
        {
            var statusPayload = JsonSerializer.Serialize(new
            {
                type = "status",
                status = task.Status.ToString(),
                phase = task.Phase?.ToString(),
                incremental = true
            }, ss);
            await Response.WriteAsync($"data: {statusPayload}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            var index = 0;
            var storeCount = analysisStore.Count(taskId);
            var metaPayload = JsonSerializer.Serialize(new
            {
                type = "meta",
                total = storeCount,
                incremental = true
            }, ss);
            await Response.WriteAsync($"data: {metaPayload}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            await foreach (var item in analysisStore.SubscribeAsync(taskId, ct))
            {
                index++;
                // 写入时延: 控制播放速度
                if (delayMs > 0) await Task.Delay(delayMs, ct);
                await WriteItemAsync(Response, item, ss, ct);
            }

            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        // 任务未完成且无 Channel 时发空完成信号
        if (task is null || task.Status != Core.Models.BacktestTaskStatus.Completed)
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

        // 已完成的任务: 从 DB 读取全量数据后流式推送
        var allResult = await getBacktestAnalysisAll.ExecuteAsync(new GetBacktestAnalysisAllQuery(taskId), ct);
        var allData = allResult.Success ? allResult.Data! : [];

        if (allData.Length == 0)
        {
            await WriteCompleteAsync(Response, ss, ct);
            return;
        }

        var completedTotal = allData.Length;
        var completedMeta = JsonSerializer.Serialize(new { type = "meta", total = completedTotal }, ss);
        await Response.WriteAsync($"data: {completedMeta}\n\n", ct);
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
