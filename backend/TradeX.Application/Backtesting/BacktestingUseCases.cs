using TradeX.Application.Common;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Application.Backtesting;

public sealed record BacktestTaskDto(
    Guid Id,
    string StrategyName,
    string Pair,
    string Status,
    string? Phase,
    decimal InitialCapital,
    DateTime CreatedAt,
    DateTime? CompletedAt);

public sealed record GetBacktestTasksQuery(Guid? StrategyId, Guid CurrentUserId);

/// <summary>获取回测任务列表用例。</summary>
public sealed class GetBacktestTasksUseCase(
    IBacktestTaskRepository taskRepo) : IUseCase<GetBacktestTasksQuery, Result<List<BacktestTaskDto>>>
{
    public async Task<Result<List<BacktestTaskDto>>> ExecuteAsync(GetBacktestTasksQuery query, CancellationToken ct = default)
    {
        var tasks = query.StrategyId.HasValue
            ? await taskRepo.GetByStrategyIdAsync(query.StrategyId.Value, ct)
            : await taskRepo.GetAllAsync(ct);

        var dtos = tasks.Select(MapToDto).ToList();
        return Result<List<BacktestTaskDto>>.Ok(dtos);
    }

    private static BacktestTaskDto MapToDto(Core.Models.BacktestTask t) => new(
        t.Id, t.StrategyName, t.Pair,
        t.Status.ToString(), t.Phase?.ToString(),
        t.InitialCapital, t.CreatedAt, t.CompletedAt);
}

public sealed record GetBacktestTaskByIdQuery(Guid Id, Guid CurrentUserId);

/// <summary>获取单个回测任务详情用例。</summary>
public sealed class GetBacktestTaskByIdUseCase(
    IBacktestTaskRepository taskRepo) : IUseCase<GetBacktestTaskByIdQuery, Result<BacktestTaskDto>>
{
    public async Task<Result<BacktestTaskDto>> ExecuteAsync(GetBacktestTaskByIdQuery query, CancellationToken ct = default)
    {
        var task = await taskRepo.GetByIdAsync(query.Id, ct);
        if (task is null)
            return Result<BacktestTaskDto>.NotFound("回测任务不存在");

        return Result<BacktestTaskDto>.Ok(new BacktestTaskDto(
            task.Id, task.StrategyName, task.Pair,
            task.Status.ToString(), task.Phase?.ToString(),
            task.InitialCapital, task.CreatedAt, task.CompletedAt));
    }
}

public sealed record CancelBacktestCommand(Guid TaskId, Guid CurrentUserId);

/// <summary>取消回测任务用例。</summary>
public sealed class CancelBacktestUseCase(
    IBacktestTaskRepository taskRepo) : IUseCase<CancelBacktestCommand, Result>
{
    public async Task<Result> ExecuteAsync(CancelBacktestCommand cmd, CancellationToken ct = default)
    {
        var task = await taskRepo.GetByIdAsync(cmd.TaskId, ct);
        if (task is null)
            return Result.NotFound("回测任务不存在");

        if (task.Status is Core.Models.BacktestTaskStatus.Completed or Core.Models.BacktestTaskStatus.Failed)
            return Result.BadRequest("任务已处于终态，无法取消");

        task.Cancel();
        await taskRepo.UpdateAsync(task, ct);

        return Result.Ok();
    }
}

public sealed record GetBacktestAnalysisPageQuery(Guid TaskId, int Page, int PageSize, string? ActionFilter);

public sealed record BacktestAnalysisPageDto(
    int Total, int Page, int PageSize, int TotalPages,
    List<BacktestKlineAnalysis> Items);

/// <summary>获取回测分析分页数据用例。</summary>
public sealed class GetBacktestAnalysisPageUseCase(
    IBacktestTaskRepository taskRepo) : IUseCase<GetBacktestAnalysisPageQuery, Result<BacktestAnalysisPageDto>>
{
    public async Task<Result<BacktestAnalysisPageDto>> ExecuteAsync(GetBacktestAnalysisPageQuery query, CancellationToken ct = default)
    {
        var items = await taskRepo.GetKlineAnalysesPageAsync(query.TaskId, query.Page, query.PageSize, query.ActionFilter, ct);
        var total = await taskRepo.GetKlineAnalysesCountAsync(query.TaskId, query.ActionFilter, ct);
        var totalPages = (int)Math.Ceiling((double)total / query.PageSize);

        return Result<BacktestAnalysisPageDto>.Ok(new BacktestAnalysisPageDto(
            total, query.Page, query.PageSize, totalPages, [..items]));
    }
}

public sealed record GetBacktestAnalysisAllQuery(Guid TaskId);

/// <summary>获取全部回测分析数据用例。</summary>
public sealed class GetBacktestAnalysisAllUseCase(
    IBacktestTaskRepository taskRepo) : IUseCase<GetBacktestAnalysisAllQuery, Result<BacktestKlineAnalysis[]>>
{
    public async Task<Result<BacktestKlineAnalysis[]>> ExecuteAsync(GetBacktestAnalysisAllQuery query, CancellationToken ct = default)
    {
        var items = await taskRepo.GetKlineAnalysesAllAsync(query.TaskId, ct);
        return Result<BacktestKlineAnalysis[]>.Ok(items);
    }
}

public sealed record GetBacktestAnalysisCountQuery(Guid TaskId);

/// <summary>获取回测分析数据数量用例。</summary>
public sealed class GetBacktestAnalysisCountUseCase(
    IBacktestTaskRepository taskRepo) : IUseCase<GetBacktestAnalysisCountQuery, Result<int>>
{
    public async Task<Result<int>> ExecuteAsync(GetBacktestAnalysisCountQuery query, CancellationToken ct = default)
    {
        var count = await taskRepo.GetKlineAnalysesCountAsync(query.TaskId, null, ct);
        return Result<int>.Ok(count);
    }
}
