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
