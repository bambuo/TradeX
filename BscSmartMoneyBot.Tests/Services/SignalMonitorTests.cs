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

public class SignalMonitorTests
{
    [Fact]
    public async Task FilterSignalsAsync_MixedInput_ReturnsOnlyQualifiedSignals()
    {
        var settings = TestBotSettingsFactory.Create();
        settings.Signals.MinMarketCap = 50_000m;
        settings.Signals.MinLiquidity = 100_000m;
        settings.Signals.MaxSoldRatio = 85m;
        settings.Signals.MinSmartMoneyWallets = 3;

        var monitor = CreateMonitor(settings);
        IReadOnlyList<Signal> signals =
        [
            new Signal
            {
                TokenAddress = "0xgood",
                TokenSymbol = "GOOD",
                MarketCap = 80_000m,
                Liquidity = 150_000m,
                SoldRatio = 30m,
                SmartMoneyWallets = 4
            },
            new Signal
            {
                TokenAddress = "0xbad",
                TokenSymbol = "BAD",
                MarketCap = 40_000m,
                Liquidity = 90_000m,
                SoldRatio = 90m,
                SmartMoneyWallets = 1
            }
        ];

        var filtered = await monitor.FilterSignalsAsync(signals, CancellationToken.None);

        var candidate = Assert.Single(filtered);
        Assert.Equal("GOOD", candidate.TokenSymbol);
    }

    private static SignalMonitor CreateMonitor(BotSettings settings)
    {
        var onchainLogger = Substitute.For<ILogger<OnchainOSClient>>();
        var monitorLogger = Substitute.For<ILogger<SignalMonitor>>();
        var stateManager = Substitute.For<IStateManager>();
        var onchainClient = new OnchainOSClient(onchainLogger, Options.Create(settings));
        return new SignalMonitor(onchainClient, Options.Create(settings), stateManager, monitorLogger);
    }
}
