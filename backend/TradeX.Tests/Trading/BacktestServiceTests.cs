using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class BacktestServiceTests
{
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var taskRepo = Substitute.For<IBacktestTaskRepository>();
        var strategyRepo = Substitute.For<IStrategyRepository>();
        var exchangeRepo = Substitute.For<IExchangeRepository>();
        var clientFactory = Substitute.For<IExchangeClientFactory>();
        var encryption = Substitute.For<IEncryptionService>();
        var queue = Substitute.For<IBacktestTaskQueue>();
        queue.EnqueueAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(ValueTask.CompletedTask);

        services.AddSingleton(taskRepo);
        services.AddSingleton(strategyRepo);
        services.AddSingleton(exchangeRepo);
        services.AddSingleton(clientFactory);
        services.AddSingleton(encryption);
        services.AddSingleton(queue);
        services.AddSingleton<IIndicatorService>(_ => new IndicatorService());
        services.AddSingleton<IConditionTreeEvaluator, ConditionTreeEvaluator>();
        services.AddScoped<IConditionEvaluator, ConditionEvaluator>();
        services.AddScoped<IBacktestService, BacktestService>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task StartBacktestAsync_StrategyNotFound_Throws()
    {
        using var sp = BuildProvider();
        var strategyRepo = sp.GetRequiredService<IStrategyRepository>();
        strategyRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Strategy?)null);

        var service = sp.GetRequiredService<IBacktestService>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.StartBacktestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT", "1h", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow));
    }

    [Fact]
    public async Task StartBacktestAsync_NoEntryCondition_Throws()
    {
        using var sp = BuildProvider();
        var strategyRepo = sp.GetRequiredService<IStrategyRepository>();
        strategyRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Strategy { EntryConditionJson = "{}" });

        var service = sp.GetRequiredService<IBacktestService>();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.StartBacktestAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT", "1h", DateTime.UtcNow.AddDays(-30), DateTime.UtcNow));
    }

    [Fact]
    public async Task StartBacktestAsync_ReturnsPendingTask()
    {
        using var sp = BuildProvider();
        var strategyId = Guid.NewGuid();
        var strategyRepo = sp.GetRequiredService<IStrategyRepository>();
        strategyRepo.GetByIdAsync(strategyId, Arg.Any<CancellationToken>())
            .Returns(new Strategy
            {
                Id = strategyId,
                Name = "测试策略",
                EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":30}""",
                CreatedBy = Guid.NewGuid()
            });

        var taskRepo = sp.GetRequiredService<IBacktestTaskRepository>();
        taskRepo.When(x => x.AddAsync(Arg.Any<BacktestTask>(), Arg.Any<CancellationToken>()))
            .Do(call =>
            {
                var task = call.Arg<BacktestTask>();
                Assert.Equal(BacktestTaskStatus.Pending, task.Status);
                Assert.Equal("BTCUSDT", task.SymbolId);
                Assert.Equal("1h", task.Timeframe);
                Assert.Equal(2000m, task.InitialCapital);
            });

        var service = sp.GetRequiredService<IBacktestService>();
        var result = await service.StartBacktestAsync(Guid.NewGuid(), strategyId, Guid.NewGuid(), "BTCUSDT", "1h",
            DateTime.UtcNow.AddDays(-30), DateTime.UtcNow, 2000m);

        Assert.Equal(BacktestTaskStatus.Pending, result.Status);
        _ = sp.GetRequiredService<IBacktestTaskQueue>().Received(1).EnqueueAsync(result.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsTask()
    {
        using var sp = BuildProvider();
        var taskId = Guid.NewGuid();
        var expected = new BacktestTask { Id = taskId };

        var repo = sp.GetRequiredService<IBacktestTaskRepository>();
        repo.GetByIdAsync(taskId, Arg.Any<CancellationToken>()).Returns(expected);

        var service = sp.GetRequiredService<IBacktestService>();
        var result = await service.GetTaskAsync(taskId);

        Assert.Equal(taskId, result!.Id);
    }

    [Fact]
    public async Task GetResultAsync_ReturnsResult()
    {
        using var sp = BuildProvider();
        var taskId = Guid.NewGuid();
        var expected = new BacktestResult { TaskId = taskId, TotalTrades = 5 };

        var repo = sp.GetRequiredService<IBacktestTaskRepository>();
        repo.GetResultByTaskIdAsync(taskId, Arg.Any<CancellationToken>()).Returns(expected);

        var service = sp.GetRequiredService<IBacktestService>();
        var result = await service.GetResultAsync(taskId);

        Assert.Equal(5, result!.TotalTrades);
    }

    [Fact]
    public async Task GetTasksByStrategyAsync_ReturnsTasks()
    {
        using var sp = BuildProvider();
        var strategyId = Guid.NewGuid();
        var tasks = new List<BacktestTask>
        {
            new() { Id = Guid.NewGuid(), StrategyId = strategyId },
            new() { Id = Guid.NewGuid(), StrategyId = strategyId }
        };

        var repo = sp.GetRequiredService<IBacktestTaskRepository>();
        repo.GetByStrategyIdAsync(strategyId, Arg.Any<CancellationToken>()).Returns(tasks);

        var service = sp.GetRequiredService<IBacktestService>();
        var result = await service.GetTasksByStrategyAsync(strategyId);

        Assert.Equal(2, result.Count);
    }
}
