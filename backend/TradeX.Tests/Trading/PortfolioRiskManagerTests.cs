using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class PortfolioRiskManagerTests
{
    private readonly IPositionRepository _positionRepo;
    private readonly IOptions<RiskSettings> _settings;
    private readonly PortfolioRiskManager _manager;

    public PortfolioRiskManagerTests()
    {
        _positionRepo = Substitute.For<IPositionRepository>();
        _settings = Options.Create(new RiskSettings
        {
            MaxDailyLoss = 1000,
            MaxDrawdownPercent = 20,
            MaxConsecutiveLosses = 3,
            MaxOpenPositions = 10,
            SlippageTolerance = 0.001m,
            MaxSlippageAmount = 10,
            CooldownSeconds = 300
        });

        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TradeX.Core.Models.Exchange?>(new TradeX.Core.Models.Exchange
            {
                Type = ExchangeType.Binance,
                ApiKeyEncrypted = "enc-key",
                SecretKeyEncrypted = "enc-secret"
            }));

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        var exchangeClient = Substitute.For<IExchangeClient>();
        exchangeClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new ConnectionTestResult(true, null, "OK"));
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(exchangeClient);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt(Arg.Any<string>()).Returns("decrypted");

        _manager = new PortfolioRiskManager(
            _positionRepo,
            new DailyLossHandler(Substitute.For<ILogger<DailyLossHandler>>()),
            new DrawdownHandler(Substitute.For<ILogger<DrawdownHandler>>()),
            new ConsecutiveLossHandler(Substitute.For<ILogger<ConsecutiveLossHandler>>()),
            new CircuitBreakerHandler(Substitute.For<ILogger<CircuitBreakerHandler>>()),
            new CooldownCheck(Substitute.For<ILogger<CooldownCheck>>()),
            new PositionLimitHandler(Substitute.For<ILogger<PositionLimitHandler>>()),
            new SlippageHandler(Substitute.For<ILogger<SlippageHandler>>()),
            new ExchangeHealthHandler(clientFactory, exchangeRepo, encryption, Substitute.For<ILogger<ExchangeHealthHandler>>()),
            _settings);
    }

    [Fact]
    public async Task CheckAsync_NoPositions_NoClosedTrades_ReturnsAllowed()
    {
        _positionRepo.GetOpenByTraderIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        _positionRepo.GetClosedByTraderIdSinceAsync(Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _manager.CheckAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_WithProfitClosedTrades_ReturnsAllowed()
    {
        var traderId = Guid.NewGuid();
        _positionRepo.GetOpenByTraderIdAsync(traderId, Arg.Any<CancellationToken>())
            .Returns([]);
        _positionRepo.GetClosedByTraderIdSinceAsync(traderId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new Position { RealizedPnl = 100 },
                new Position { RealizedPnl = 50 }
            ]);

        var result = await _manager.CheckAsync(traderId, Guid.NewGuid());

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_DailyLossOverLimit_Denies()
    {
        var traderId = Guid.NewGuid();
        _positionRepo.GetOpenByTraderIdAsync(traderId, Arg.Any<CancellationToken>())
            .Returns([]);
        _positionRepo.GetClosedByTraderIdSinceAsync(traderId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new Position { RealizedPnl = -800 },
                new Position { RealizedPnl = -500 }
            ]);

        var result = await _manager.CheckAsync(traderId, Guid.NewGuid());

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("当日亏损"));
    }

    [Fact]
    public async Task CheckAsync_PositionLimitExceeded_Denies()
    {
        var traderId = Guid.NewGuid();
        _positionRepo.GetOpenByTraderIdAsync(traderId, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 15).Select(_ => new Position
            {
                Status = PositionStatus.Open,
                CurrentPrice = 100,
                Quantity = 1
            }).ToList());
        _positionRepo.GetClosedByTraderIdSinceAsync(traderId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _manager.CheckAsync(traderId, Guid.NewGuid());

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("持仓数量"));
    }

    [Fact]
    public async Task CheckAsync_MultipleViolations_CollectsAllReasons()
    {
        var traderId = Guid.NewGuid();
        _positionRepo.GetOpenByTraderIdAsync(traderId, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 15).Select(_ => new Position
            {
                Status = PositionStatus.Open,
                CurrentPrice = 100,
                Quantity = 1
            }).ToList());
        _positionRepo.GetClosedByTraderIdSinceAsync(traderId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new Position { RealizedPnl = -800 },
                new Position { RealizedPnl = -500 }
            ]);

        var result = await _manager.CheckAsync(traderId, Guid.NewGuid());

        Assert.False(result.IsAllowed);
        Assert.True(result.DeniedReasons.Count >= 2);
    }

    [Fact]
    public async Task CheckSymbolRiskAsync_SetsSymbolId()
    {
        var traderId = Guid.NewGuid();
        var symbolId = "BTCUSDT";
        _positionRepo.GetOpenByTraderIdAsync(traderId, Arg.Any<CancellationToken>())
            .Returns([]);
        _positionRepo.GetClosedByTraderIdSinceAsync(traderId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _manager.CheckSymbolRiskAsync(traderId, Guid.NewGuid(), symbolId);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_NearLimit_Allows()
    {
        var traderId = Guid.NewGuid();
        _positionRepo.GetOpenByTraderIdAsync(traderId, Arg.Any<CancellationToken>())
            .Returns(Enumerable.Range(0, 9).Select(_ => new Position
            {
                Status = PositionStatus.Open,
                CurrentPrice = 1000,
                Quantity = 1
            }).ToList());
        _positionRepo.GetClosedByTraderIdSinceAsync(traderId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns([
                new Position { RealizedPnl = -100 }
            ]);

        var result = await _manager.CheckAsync(traderId, Guid.NewGuid());

        // DailyLoss = 100 < 1000, drawdown = 100/9000*100 = 1.1% < 20%, positions = 9 < 10
        Assert.True(result.IsAllowed);
    }
}
