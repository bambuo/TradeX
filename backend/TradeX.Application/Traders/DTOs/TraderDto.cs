namespace TradeX.Application.Traders.DTOs;

public sealed record TraderDto(
    Guid Id,
    string Name,
    string Status,
    string? AvatarColor,
    string? AvatarUrl,
    string? Style,
    DateTime CreatedAt,
    DateTime? UpdatedAt);

public sealed record TraderStatsDto(
    int TotalTrades,
    decimal WinRate,
    decimal ProfitLossRatio,
    decimal SharpeRatio);
