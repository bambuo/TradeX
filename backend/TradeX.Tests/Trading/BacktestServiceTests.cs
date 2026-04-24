using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Indicators;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class BacktestServiceTests
{
    private readonly IBacktestTaskRepository _taskRepo;
    private readonly IStrategyRepository _strategyRepo;
    private readonly IExchangeAccountRepository _accountRepo;
    private readonly IExchangeClientFactory _clientFactory;
    private readonly IEncryptionService _encryption;
    private readonly BacktestService _service;

    public BacktestServiceTests()
    {
        _taskRepo = Substitute.For<IBacktestTaskRepository>();
        _strategyRepo = Substitute.For<IStrategyRepository>();
        _accountRepo = Substitute.For<IExchangeAccountRepository>();
        _clientFactory = Substitute.For<IExchangeClientFactory>();
        _encryption = Substitute.For<IEncryptionService>();

        var indicatorService = new IndicatorService();
        var treeEvaluator = new ConditionTreeEvaluator();
        var conditionEvaluator = new ConditionEvaluator(treeEvaluator);
        var engine = new BacktestEngine(indicatorService, conditionEvaluator);

        _service = new BacktestService(
            _taskRepo, _strategyRepo, _accountRepo,
            _clientFactory, _encryption, engine,
            Substitute.For<ILogger<BacktestService>>());
    }

    [Fact]
    public async Task StartBacktestAsync_StrategyNotFound_Throws()
    {
        _strategyRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Strategy?)null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartBacktestAsync(Guid.NewGuid(), DateTime.UtcNow.AddDays(-30), DateTime.UtcNow));
    }

    [Fact]
    public async Task StartBacktestAsync_NoEntryCondition_Throws()
    {
        _strategyRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Strategy
            {
                EntryConditionJson = "{}",
                SymbolIds = "BTCUSDT",
                Timeframe = "1h"
            });

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.StartBacktestAsync(Guid.NewGuid(), DateTime.UtcNow.AddDays(-30), DateTime.UtcNow));
    }

    [Fact]
    public async Task StartBacktestAsync_SavesTask_OnFailure()
    {
        var strategyId = Guid.NewGuid();
        _strategyRepo.GetByIdAsync(strategyId, Arg.Any<CancellationToken>())
            .Returns(new Strategy
            {
                Id = strategyId,
                EntryConditionJson = """{"Operator":"","Indicator":"RSI","Comparison":">","Value":30}""",
                SymbolIds = "BTCUSDT",
                Timeframe = "1h",
                ExchangeId = Guid.NewGuid(),
                CreatedBy = Guid.NewGuid()
            });

        _accountRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ExchangeAccount?)null);

        var result = await _service.StartBacktestAsync(strategyId, DateTime.UtcNow.AddDays(-30), DateTime.UtcNow);

        Assert.Equal(BacktestTaskStatus.Failed, result.Status);
        await _taskRepo.Received(1).AddAsync(Arg.Any<BacktestTask>(), Arg.Any<CancellationToken>());
        await _taskRepo.Received(2).UpdateAsync(Arg.Any<BacktestTask>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTaskAsync_ReturnsTask()
    {
        var taskId = Guid.NewGuid();
        var expected = new BacktestTask { Id = taskId };
        _taskRepo.GetByIdAsync(taskId, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.GetTaskAsync(taskId);

        Assert.Equal(taskId, result!.Id);
    }

    [Fact]
    public async Task GetResultAsync_ReturnsResult()
    {
        var taskId = Guid.NewGuid();
        var expected = new BacktestResult { TaskId = taskId, TotalTrades = 5 };
        _taskRepo.GetResultByTaskIdAsync(taskId, Arg.Any<CancellationToken>())
            .Returns(expected);

        var result = await _service.GetResultAsync(taskId);

        Assert.Equal(5, result!.TotalTrades);
    }

    [Fact]
    public async Task GetTasksByStrategyAsync_ReturnsTasks()
    {
        var strategyId = Guid.NewGuid();
        var tasks = new List<BacktestTask>
        {
            new() { Id = Guid.NewGuid(), StrategyId = strategyId },
            new() { Id = Guid.NewGuid(), StrategyId = strategyId }
        };
        _taskRepo.GetByStrategyIdAsync(strategyId, Arg.Any<CancellationToken>())
            .Returns(tasks);

        var result = await _service.GetTasksByStrategyAsync(strategyId);

        Assert.Equal(2, result.Count);
    }
}
