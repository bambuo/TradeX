namespace TradeX.Application.Common;

/// <summary>仪表盘统计服务。</summary>
public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default);
}

public sealed record DashboardSummary(
    int TraderCount,
    int StrategyCount,
    int ActiveStrategyCount,
    int OpenPositionCount,
    int TodayOrderCount,
    decimal TotalBalance,
    decimal TotalPnl,
    decimal WinRate,
    Dictionary<string, string?> ExchangeStatus,
    IReadOnlyList<RecentTradeDto> RecentTrades);

public sealed record RecentTradeDto(
    Guid OrderId,
    string Pair,
    string Side,
    decimal Quantity,
    decimal Price,
    DateTime PlacedAtUtc);

/// <summary>系统初始化服务。</summary>
public interface ISetupService
{
    Task<bool> GetStatusAsync(CancellationToken ct = default);
    Task<Result> InitializeAsync(string userName, string password, string? jwtSecret, CancellationToken ct = default);
}

/// <summary>系统服务 — 封装紧急停止等涉及多个基础设施服务的操作。</summary>
public interface ISystemService
{
    Task<EmergencyStopResultDto> EmergencyStopAsync(Guid currentUserId, CancellationToken ct = default);
}

public sealed record EmergencyStopResultDto(
    bool Success, int DisabledExchanges, int CancelledOrders, string Message);
