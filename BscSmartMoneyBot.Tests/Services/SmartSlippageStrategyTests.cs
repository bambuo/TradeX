using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Trading.Strategies;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Tests.Services;

public class SmartSlippageStrategyTests
{
    [Fact]
    public void CalculateSmartSlippagePercent_SmartDisabled_ReturnsDefaultSlippage()
    {
        var settings = new BotSettings();
        settings.Slippage.SmartSlippageEnabled = false;
        settings.Trading.DefaultSlippage = 0.20m;
        var strategy = new SmartSlippageStrategy(Options.Create(settings));
        var signal = new Signal { Liquidity = 1_000_000m, MarketCap = 2_000_000m };

        var slippage = strategy.CalculateSmartSlippagePercent(signal, 100m, isSell: false);

        Assert.Equal(0.20m, slippage);
    }
}
