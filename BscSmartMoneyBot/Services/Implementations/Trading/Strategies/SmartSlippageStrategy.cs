using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Interfaces;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Services.Implementations.Trading.Strategies;

public class SmartSlippageStrategy(IOptions<BotSettings> settingsOptions) : ISlippageStrategy
{
    private readonly BotSettings _settings = settingsOptions.Value;

    public decimal CalculateSmartSlippagePercent(Signal signal, decimal tradeAmountUsd, bool isSell)
    {
        if (!_settings.Slippage.SmartSlippageEnabled)
        {
            return Math.Clamp(_settings.Trading.DefaultSlippage, _settings.Slippage.MinSlippagePercent, _settings.Slippage.MaxSlippagePercent);
        }

        var baseSlippage = isSell ? _settings.Slippage.BaseSellSlippagePercent : _settings.Slippage.BaseBuySlippagePercent;
        var adjustment = 1.0m;

        if (signal.Liquidity <= 0 || signal.Liquidity < _settings.Slippage.LiquidityThresholdUSD)
        {
            adjustment *= 1.30m;
        }
        else if (signal.Liquidity >= _settings.Slippage.LiquidityThresholdUSD * 5m)
        {
            adjustment *= 0.90m;
        }

        if (signal.SoldRatio > 70m)
        {
            adjustment *= 1.10m;
        }

        if (signal.MarketCap < 300000m)
        {
            adjustment *= 1.15m;
        }

        if (signal.MarketCap > 1000000m)
        {
            adjustment *= 0.90m;
        }

        if (tradeAmountUsd > 0 && signal.Liquidity > 0)
        {
            var tradeRatio = tradeAmountUsd / signal.Liquidity;
            if (tradeRatio > 0.005m)
            {
                adjustment *= Math.Min(1.25m, 1.0m + tradeRatio * 20m);
            }
        }

        var slippage = baseSlippage * adjustment;
        slippage = Math.Clamp(slippage, _settings.Slippage.MinSlippagePercent, _settings.Slippage.MaxSlippagePercent);
        return Math.Round(slippage, 4);
    }
}
