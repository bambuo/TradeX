using Microsoft.Extensions.Options;
using TradeX.Core.Interfaces;

namespace TradeX.Trading;

public class PortfolioRiskManager(
    IPositionRepository positionRepo,
    DailyLossHandler dailyLossHandler,
    DrawdownHandler drawdownHandler,
    ConsecutiveLossHandler consecutiveLossHandler,
    CircuitBreakerHandler circuitBreakerHandler,
    CooldownCheck cooldownCheck,
    PositionLimitHandler positionLimitHandler,
    SlippageHandler slippageHandler,
    ExchangeHealthHandler exchangeHealthHandler,
    IOptions<RiskSettings> riskSettings) : IPortfolioRiskManager
{
    public async Task<RiskResult> CheckAsync(Guid traderId, Guid exchangeId, CancellationToken ct = default)
    {
        var chain = BuildChain();
        var context = await BuildContextAsync(traderId, exchangeId, null, ct);
        var result = await chain.CheckAsync(context, ct);
        return BuildResult(result);
    }

    public async Task<RiskResult> CheckPairRiskAsync(Guid traderId, Guid exchangeId, string pair, CancellationToken ct = default)
    {
        var chain = BuildChain();
        var context = await BuildContextAsync(traderId, exchangeId, pair, ct);
        var result = await chain.CheckAsync(context, ct);
        return BuildResult(result);
    }

    private static RiskResult BuildResult(RiskContext context)
        => new(context.IsAllowed, context.DeniedReasons.AsReadOnly());

    private IRiskCheckHandler BuildChain()
    {
        dailyLossHandler.SetNext(drawdownHandler)
            .SetNext(consecutiveLossHandler)
            .SetNext(circuitBreakerHandler)
            .SetNext(cooldownCheck)
            .SetNext(positionLimitHandler)
            .SetNext(slippageHandler)
            .SetNext(exchangeHealthHandler);

        return dailyLossHandler;
    }

    private async Task<RiskContext> BuildContextAsync(Guid traderId, Guid exchangeId, string? Pair, CancellationToken ct)
    {
        var openPositions = await positionRepo.GetOpenByTraderIdAsync(traderId, ct);
        var todayStart = DateTime.UtcNow.Date;
        var settings = riskSettings.Value;

        var closedToday = await positionRepo.GetClosedByTraderIdSinceAsync(traderId, todayStart, ct);
        var dailyRealizedPnl = closedToday.Sum(p => p.RealizedPnl);
        var dailyLoss = Math.Abs(Math.Min(dailyRealizedPnl, 0));
        var dailyProfit = Math.Max(dailyRealizedPnl, 0);

        var consecutiveLosses = closedToday
            .Take(settings.MaxConsecutiveLosses)
            .TakeWhile(p => p.RealizedPnl < 0)
            .Count();

        var totalPortfolioValue = openPositions.Sum(p => p.CurrentPrice * p.Quantity);

        return new RiskContext
        {
            TraderId = traderId,
            ExchangeId = exchangeId,
            Pair = Pair,
            PortfolioValue = totalPortfolioValue,
            DailyLoss = dailyLoss,
            DailyProfit = dailyProfit,
            ConsecutiveLossCount = consecutiveLosses,
            OpenPositionCount = openPositions.Count,
            MaxDailyLoss = settings.MaxDailyLoss,
            MaxDrawdownPercent = settings.MaxDrawdownPercent,
            MaxConsecutiveLosses = settings.MaxConsecutiveLosses,
            MaxOpenPositions = settings.MaxOpenPositions,
            SlippageTolerance = settings.SlippageTolerance,
            MaxSlippageAmount = settings.MaxSlippageAmount,
            CircuitBreakerActive = settings.CircuitBreakerActive,
            CooldownSeconds = settings.CooldownSeconds
        };
    }
}

public interface IPortfolioRiskManager
{
    Task<RiskResult> CheckAsync(Guid traderId, Guid exchangeId, CancellationToken ct = default);
    Task<RiskResult> CheckPairRiskAsync(Guid traderId, Guid exchangeId, string pair, CancellationToken ct = default);
}

public record RiskResult(bool IsAllowed, IReadOnlyList<string> DeniedReasons);
