using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Services.Implementations.Trading.Strategies;

public class PositionSizingStrategy(IOptions<BotSettings> settingsOptions) : IPositionSizingStrategy
{
    private readonly BotSettings _settings = settingsOptions.Value;

    public decimal CalculateSignalScore(Signal signal)
    {
        if (!_settings.Adaptive.PositionScalingEnabled)
        {
            return 0.5m;
        }

        var liquidityScore = Math.Clamp(signal.Liquidity / Math.Max(_settings.Signals.MinLiquidity, 1m), 0m, 2m) / 2m;
        var marketCapScore = Math.Clamp(signal.MarketCap / Math.Max(_settings.Signals.MinMarketCap, 1m), 0m, 3m) / 3m;
        var walletScore = Math.Clamp(signal.SmartMoneyWallets / Math.Max((decimal)_settings.Signals.MinSmartMoneyWallets, 1m), 0m, 2m) / 2m;
        var soldPenalty = 1m - Math.Clamp(signal.SoldRatio / 100m, 0m, 1m);

        var score = (liquidityScore * 0.35m) + (marketCapScore * 0.25m) + (walletScore * 0.25m) + (soldPenalty * 0.15m);
        return Math.Round(Math.Clamp(score, 0m, 1m), 3);
    }

    public decimal GetRecommendedBuyAmount(Signal signal, decimal? accountBalanceUsd = null)
    {
        var score = CalculateSignalScore(signal);

        if (!_settings.Adaptive.DynamicPositionEnabled)
        {
            return _settings.Trading.MinPositionSizeUSD;
        }

        var min = _settings.Trading.MinPositionSizeUSD;
        var max = _settings.Trading.MaxPositionSizeUSD;
        var scaled = min + (max - min) * score;

        if (accountBalanceUsd is > 0 && _settings.Adaptive.MaxRiskPerTradePercent > 0)
        {
            var riskCap = accountBalanceUsd.Value * (_settings.Adaptive.MaxRiskPerTradePercent / 100m);
            scaled = Math.Min(scaled, Math.Max(min, riskCap));
        }

        return Math.Round(Math.Clamp(scaled, min, max), 2);
    }
}
