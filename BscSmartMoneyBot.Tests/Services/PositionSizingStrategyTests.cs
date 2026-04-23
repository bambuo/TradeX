using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Trading.Strategies;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Tests.Services;

public class PositionSizingStrategyTests
{
    [Fact]
    public void GetRecommendedBuyAmount_DynamicDisabled_ReturnsMinPositionSize()
    {
        var settings = new BotSettings();
        settings.Adaptive.DynamicPositionEnabled = false;
        settings.Trading.MinPositionSizeUSD = 7m;
        var strategy = new PositionSizingStrategy(Options.Create(settings));

        var amount = strategy.GetRecommendedBuyAmount(new Signal());

        Assert.Equal(7m, amount);
    }
}
