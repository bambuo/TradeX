using BscSmartMoneyBot.Configuration;
using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Clients;
using BscSmartMoneyBot.Services.Implementations.Trading;
using BscSmartMoneyBot.Services.Interfaces;
using BscSmartMoneyBot.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BscSmartMoneyBot.Tests.Services;

public class PositionManagerTests
{
    [Fact]
    public async Task GetAdjustedPollIntervalAsync_NoOpenPositions_ReturnsDefaultInterval()
    {
        var settings = TestBotSettingsFactory.Create();
        settings.Monitoring.PollIntervalSeconds = 9;

        var stateManager = Substitute.For<IStateManager>();
        stateManager.LoadStateAsync(Arg.Any<CancellationToken>()).Returns(new BotState());

        var manager = CreatePositionManager(settings, stateManager);

        var interval = await manager.GetAdjustedPollIntervalAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(9), interval);
    }

    private static PositionManager CreatePositionManager(BotSettings settings, IStateManager stateManager)
    {
        var onchainLogger = Substitute.For<ILogger<OnchainOSClient>>();
        var managerLogger = Substitute.For<ILogger<PositionManager>>();
        var onchainClient = new OnchainOSClient(onchainLogger, Options.Create(settings));
        var tradeExecutor = Substitute.For<ITradeExecutor>();

        return new PositionManager(
            onchainClient,
            Options.Create(settings),
            stateManager,
            tradeExecutor,
            managerLogger);
    }
}
