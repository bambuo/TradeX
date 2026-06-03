using TradeX.Application.Common;
using TradeX.Core.Interfaces;

namespace TradeX.Application.System;

public sealed record GetExchangeStatusQuery;

/// <summary>获取交易所连接状态用例。</summary>
public sealed class GetExchangeStatusUseCase(
    IExchangeRepository exchangeRepo) : IUseCase<GetExchangeStatusQuery, Result<List<ExchangeStatusDto>>>
{
    public async Task<Result<List<ExchangeStatusDto>>> ExecuteAsync(GetExchangeStatusQuery query, CancellationToken ct = default)
    {
        var exchanges = await exchangeRepo.GetAllEnabledAsync(ct);
        var dtos = exchanges.Select(e => new ExchangeStatusDto(
            e.Id, e.Name, e.Type.ToString(), e.Status.ToString())).ToList();
        return Result<List<ExchangeStatusDto>>.Ok(dtos);
    }
}

public sealed record ExchangeStatusDto(
    Guid Id, string Name, string Type, string Status);

public sealed record EmergencyStopCommand(Guid CurrentUserId);

/// <summary>紧急停止用例 — 禁用所有交易所。</summary>
public sealed class EmergencyStopUseCase(
    ISystemService systemService) : IUseCase<EmergencyStopCommand, Result<EmergencyStopResultDto>>
{
    public async Task<Result<EmergencyStopResultDto>> ExecuteAsync(EmergencyStopCommand cmd, CancellationToken ct = default)
    {
        var result = await systemService.EmergencyStopAsync(cmd.CurrentUserId, ct);
        return Result<EmergencyStopResultDto>.Ok(result);
    }
}

public sealed record GetSystemLogsQuery(int Count);

/// <summary>获取系统日志用例。</summary>
public sealed class GetSystemLogsUseCase(
    IAuditLogRepository auditLogRepo) : IUseCase<GetSystemLogsQuery, Result<List<SystemLogEntryDto>>>
{
    public async Task<Result<List<SystemLogEntryDto>>> ExecuteAsync(GetSystemLogsQuery query, CancellationToken ct = default)
    {
        var logs = await auditLogRepo.GetRecentAsync(query.Count, ct);
        var dtos = logs.Select(l => new SystemLogEntryDto(
            l.Id, l.Action, l.Resource, l.Timestamp)).ToList();
        return Result<List<SystemLogEntryDto>>.Ok(dtos);
    }
}

public sealed record SystemLogEntryDto(
    Guid Id, string Action, string Resource, DateTime Timestamp);
