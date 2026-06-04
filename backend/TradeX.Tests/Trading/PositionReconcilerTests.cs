using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Events;
using TradeX.Trading.Execution;
using TradeX.Trading.Observability;
using TradeX.Trading.Risk;

namespace TradeX.Tests.Trading;

public class PositionReconcilerTests
{
    private static readonly List<string> Quotes =
        ["USDT", "USDC", "FDUSD", "TUSD", "BUSD", "DAI", "BTC", "ETH", "BNB", "EUR", "TRY"];

    [Theory]
    [InlineData("BTCUSDT", "BTC")]
    [InlineData("ETH_USDT", "ETH")]
    [InlineData("SOL-USDC", "SOL")]
    [InlineData("ETHBTC", "ETH")]
    [InlineData("doge/usdt", "DOGE")]
    [InlineData("XYZ", null)]      // 无可识别 quote
    [InlineData("USDT", null)]     // 仅 quote 自身
    public void ResolveBaseAsset_Cases(string pair, string? expected)
    {
        var ordered = Quotes.OrderByDescending(q => q.Length).ToList();
        Assert.Equal(expected, PositionReconciler.ResolveBaseAsset(pair, ordered));
    }

    private static (PositionReconciler reconciler, IOutboxRepository outbox, IExchangeClient client)
        Build(RiskSettings settings, decimal localQty, decimal exchangeQty, Guid? traderId = null)
    {
        var exchangeId = Guid.NewGuid();
        var exchangeRepo = Substitute.For<IExchangeRepository>();
        exchangeRepo.GetAllEnabledAsync(Arg.Any<CancellationToken>()).Returns([
            new TradeX.Core.Models.Exchange { Id = exchangeId, Type = ExchangeType.Binance, TraderId = traderId,
                ApiKeyEncrypted = "k", SecretKeyEncrypted = "s", Status = ExchangeStatus.Enabled }
        ]);

        var positionRepo = Substitute.For<IPositionRepository>();
        var positions = localQty > 0
            ? new List<Position> { OpenPos(exchangeId, "BTCUSDT", localQty) }
            : [];
        positionRepo.GetAllOpenAsync(Arg.Any<CancellationToken>()).Returns(positions);

        var client = Substitute.For<IExchangeClient>();
        client.GetAssetBalancesAsync(Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, decimal> { ["BTC"] = exchangeQty });

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        clientFactory.CreateClient(Arg.Any<ExchangeType>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>())
            .Returns(client);

        var encryption = Substitute.For<IEncryptionService>();
        encryption.Decrypt(Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0) + "-dec");

        var outbox = Substitute.For<IOutboxRepository>();
        var reconciler = new PositionReconciler(exchangeRepo, positionRepo, clientFactory, encryption,
            outbox, new TradeXMetrics(), Options.Create(settings), Substitute.For<ILogger<PositionReconciler>>());

        return (reconciler, outbox, client);
    }

    private static Position OpenPos(Guid exchangeId, string pair, decimal qty)
    {
        var p = Position.Open(Guid.NewGuid(), exchangeId, Guid.NewGuid(), pair, qty, 50000m);
        return p;
    }

    [Fact]
    public async Task Overstatement_BeyondTolerance_EmitsCriticalAlert()
    {
        // 本地 1.0 BTC，实际 0.5 BTC → 漂移 +0.5 (50%) → Critical
        var (reconciler, outbox, _) = Build(new RiskSettings { PositionDriftTolerancePercent = 1m },
            localQty: 1.0m, exchangeQty: 0.5m);

        OutboxEvent? captured = null;
        await outbox.EnqueueAsync(Arg.Do<OutboxEvent>(e => captured = e), Arg.Any<CancellationToken>());

        var count = await reconciler.ReconcilePositionsAsync();

        Assert.Equal(1, count);
        Assert.NotNull(captured);
        Assert.Equal(TradingEventTypes.PositionDriftDetected, captured!.Type);
        Assert.Contains("Critical", captured.PayloadJson);
        await outbox.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithinTolerance_NoAlert()
    {
        // 本地 1.0 BTC，实际 0.999 BTC → 0.1% < 1% → 无告警
        var (reconciler, outbox, _) = Build(new RiskSettings { PositionDriftTolerancePercent = 1m },
            localQty: 1.0m, exchangeQty: 0.999m);

        var count = await reconciler.ReconcilePositionsAsync();

        Assert.Equal(0, count);
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Surplus_SuppressedByDefault()
    {
        // 实际 2.0 > 本地 1.0（盈余方向）→ 默认 PositionDriftReportSurplus=false → 不告警
        var (reconciler, outbox, _) = Build(new RiskSettings { PositionDriftTolerancePercent = 1m },
            localQty: 1.0m, exchangeQty: 2.0m);

        var count = await reconciler.ReconcilePositionsAsync();

        Assert.Equal(0, count);
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Surplus_ReportedWhenEnabled_AsWarning()
    {
        var (reconciler, outbox, _) = Build(
            new RiskSettings { PositionDriftTolerancePercent = 1m, PositionDriftReportSurplus = true },
            localQty: 1.0m, exchangeQty: 2.0m);

        OutboxEvent? captured = null;
        await outbox.EnqueueAsync(Arg.Do<OutboxEvent>(e => captured = e), Arg.Any<CancellationToken>());

        var count = await reconciler.ReconcilePositionsAsync();

        Assert.Equal(1, count);
        Assert.Contains("Warning", captured!.PayloadJson);
    }

    [Fact]
    public async Task Disabled_NoOp()
    {
        var (reconciler, outbox, client) = Build(
            new RiskSettings { PositionReconcileEnabled = false },
            localQty: 1.0m, exchangeQty: 0.1m);

        var count = await reconciler.ReconcilePositionsAsync();

        Assert.Equal(0, count);
        await client.DidNotReceive().GetAssetBalancesAsync(Arg.Any<CancellationToken>());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MinAbsolute_FiltersDust()
    {
        // 漂移 0.4%（超 0.1% 容差），但绝对值 0.004 ≤ minAbs 0.01 → 视为粉尘忽略
        var (reconciler, outbox, _) = Build(
            new RiskSettings { PositionDriftTolerancePercent = 0.1m, PositionDriftMinAbsolute = 0.01m },
            localQty: 1.0m, exchangeQty: 0.996m);

        var count = await reconciler.ReconcilePositionsAsync();

        Assert.Equal(0, count);
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
    }
}
