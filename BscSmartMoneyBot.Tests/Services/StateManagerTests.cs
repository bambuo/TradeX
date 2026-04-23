using BscSmartMoneyBot.Core.Models;
using BscSmartMoneyBot.Services.Implementations.Clients;
using BscSmartMoneyBot.Services.Implementations.Persistence;
using BscSmartMoneyBot.Tests.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace BscSmartMoneyBot.Tests.Services;

public class StateManagerTests
{
    [Fact]
    public async Task LoadStateAsync_FileMissing_ReturnsDefaultState()
    {
        var testDir = CreateUniqueTempDirectory();
        try
        {
            var manager = CreateStateManager(testDir);

            var state = await manager.LoadStateAsync(CancellationToken.None);

            Assert.NotNull(state);
            Assert.Empty(state.OpenPositions);
            Assert.Empty(state.SeenSignals);
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveStateAsync_ThenLoadStateAsync_RoundTripsState()
    {
        var testDir = CreateUniqueTempDirectory();
        try
        {
            var manager = CreateStateManager(testDir);
            var state = new BotState();
            state.SeenSignals["0xabc"] = DateTime.UtcNow;

            await manager.SaveStateAsync(state, CancellationToken.None);
            var loaded = await manager.LoadStateAsync(CancellationToken.None);

            Assert.Single(loaded.SeenSignals);
            Assert.True(loaded.SeenSignals.ContainsKey("0xabc"));
        }
        finally
        {
            Directory.Delete(testDir, recursive: true);
        }
    }

    private static StateManager CreateStateManager(string testDir)
    {
        var stateFile = Path.Combine(testDir, "state", "bot_state.json");
        var backupDir = Path.Combine(testDir, "state", "backup");
        var settings = TestBotSettingsFactory.Create(stateFile, backupDir);

        var stateLogger = Substitute.For<ILogger<StateManager>>();
        var onchainLogger = Substitute.For<ILogger<OnchainOSClient>>();
        var onchainClient = new OnchainOSClient(onchainLogger, Options.Create(settings));

        return new StateManager(stateLogger, onchainClient, Options.Create(settings));
    }

    private static string CreateUniqueTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "bsc-bot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
