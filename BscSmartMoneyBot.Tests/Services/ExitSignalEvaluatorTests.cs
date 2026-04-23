using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Trading.Strategies;
using Microsoft.Extensions.Options;

namespace BscSmartMoneyBot.Tests.Services;

public class ExitSignalEvaluatorTests
{
    [Fact]
    public void ShouldStopLoss_PriceBelowThreshold_ReturnsTrue()
    {
        var settings = new BotSettings();
        settings.Risk.StopLossPercent = 10m;
        var evaluator = new ExitSignalEvaluator(Options.Create(settings));
        var position = new Position { EntryPriceUSD = 100m, CurrentPriceUSD = 89m };

        var shouldStopLoss = evaluator.ShouldStopLoss(position, out var stopLossPrice);

        Assert.True(shouldStopLoss);
        Assert.Equal(90m, stopLossPrice);
    }
}
