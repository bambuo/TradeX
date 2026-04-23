using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Clients;
using BscSmartMoneyBot.Services.Implementations.Trading;
using BscSmartMoneyBot.Services.Interfaces;
using BscSmartMoneyBot.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BscSmartMoneyBot.Tests.Services;

public class TradeExecutorTests
{
    [Fact]
    public void GetRecommendedBuyAmount_DelegatesToPositionSizingStrategy_ReturnsExpectedValue()
    {
        var settings = TestBotSettingsFactory.Create();
        var stateManager = Substitute.For<IStateManager>();
        var positionSizingStrategy = Substitute.For<IPositionSizingStrategy>();
        var slippageStrategy = Substitute.For<ISlippageStrategy>();
        var exitSignalEvaluator = Substitute.For<IExitSignalEvaluator>();
        var logger = Substitute.For<ILogger<TradeExecutor>>();
        var onchainLogger = Substitute.For<ILogger<OnchainOSClient>>();
        var onchainClient = new OnchainOSClient(onchainLogger, Options.Create(settings));

        positionSizingStrategy.CalculateSignalScore(Arg.Any<Signal>()).Returns(0.88m);
        positionSizingStrategy.GetRecommendedBuyAmount(Arg.Any<Signal>(), Arg.Any<decimal?>()).Returns(23.5m);

        var executor = new TradeExecutor(
            onchainClient,
            Options.Create(settings),
            stateManager,
            positionSizingStrategy,
            slippageStrategy,
            exitSignalEvaluator,
            logger);

        var signal = new Signal();
        var amount = executor.GetRecommendedBuyAmount(signal);

        Assert.Equal(23.5m, amount);
        Assert.Equal(0.88m, signal.Score);
        positionSizingStrategy.Received(1).CalculateSignalScore(signal);
        positionSizingStrategy.Received(1).GetRecommendedBuyAmount(signal, Arg.Any<decimal?>());
    }
}
