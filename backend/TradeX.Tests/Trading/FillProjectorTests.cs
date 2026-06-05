using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Execution;
using TradeX.Trading.EventBus;

namespace TradeX.Tests.Trading;

public class FillProjectorTests
{
    private static (FillProjector projector, IPositionRepository posRepo, IOrderRepository orderRepo, IDomainEventBus bus) Build()
    {
        var posRepo = Substitute.For<IPositionRepository>();
        var orderRepo = Substitute.For<IOrderRepository>();
        var bus = Substitute.For<IDomainEventBus>();
        var projector = new FillProjector(posRepo, orderRepo, bus, Substitute.For<ILogger<FillProjector>>());
        return (projector, posRepo, orderRepo, bus);
    }

    private static Order FilledBuy(decimal filledQty, decimal quoteQty = 0, Guid? strategyId = null) => new()
    {
        TraderId = Guid.NewGuid(),
        ExchangeId = Guid.NewGuid(),
        StrategyId = strategyId ?? Guid.NewGuid(),
        Pair = "BTCUSDT",
        Side = OrderSide.Buy,
        Type = OrderType.Market,
        Status = OrderStatus.Filled,
        FilledQuantity = filledQty,
        QuoteQuantity = quoteQty
    };

    [Fact]
    public async Task Buy_Filled_OpensSinglePosition_WithOpeningOrderId()
    {
        var (projector, posRepo, orderRepo, bus) = Build();
        var order = FilledBuy(filledQty: 0.5m);
        posRepo.GetByOpeningOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns((Position?)null);

        Position? captured = null;
        await posRepo.AddAsync(Arg.Do<Position>(p => captured = p), Arg.Any<CancellationToken>());

        await projector.ProjectFilledAsync(order, avgFillPrice: 60000m);

        await posRepo.Received(1).AddAsync(Arg.Any<Position>(), Arg.Any<CancellationToken>());
        Assert.NotNull(captured);
        Assert.Equal(order.Id, captured!.OpeningOrderId);
        Assert.Equal(order.StrategyId, captured.StrategyId);
        Assert.Equal(0.5m, captured.Quantity);
        Assert.Equal(60000m, captured.EntryPrice);
        Assert.Equal(PositionStatus.Open, captured.Status);
        Assert.Equal(captured.Id, order.PositionId); // 审计回链
        await bus.Received(1).PublishAsync(
            Arg.Is<PositionUpdatedPayload>(p =>
                p.PositionId == captured!.Id &&
                p.TraderId == order.TraderId &&
                p.Pair == "BTCUSDT"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Buy_Idempotent_WhenPositionAlreadyExists()
    {
        var (projector, posRepo, _, _) = Build();
        var order = FilledBuy(filledQty: 1m);
        posRepo.GetByOpeningOrderIdAsync(order.Id, Arg.Any<CancellationToken>())
            .Returns(Position.Open(order.TraderId, order.ExchangeId, order.StrategyId!.Value, order.Pair, 1m, 100m));

        await projector.ProjectFilledAsync(order, avgFillPrice: 100m);

        await posRepo.DidNotReceive().AddAsync(Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Buy_AvgPriceZero_FallsBackToQuoteOverFilled()
    {
        var (projector, posRepo, _, _) = Build();
        var order = FilledBuy(filledQty: 0.4m, quoteQty: 100m); // 100 / 0.4 = 250
        posRepo.GetByOpeningOrderIdAsync(order.Id, Arg.Any<CancellationToken>()).Returns((Position?)null);

        Position? captured = null;
        await posRepo.AddAsync(Arg.Do<Position>(p => captured = p), Arg.Any<CancellationToken>());

        await projector.ProjectFilledAsync(order, avgFillPrice: 0m);

        Assert.Equal(250m, captured!.EntryPrice);
    }

    [Fact]
    public async Task Sell_WithPositionId_ClosesThatPosition()
    {
        var (projector, posRepo, _, bus) = Build();
        var pos = Position.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT", 1m, 50000m);
        var order = new Order
        {
            TraderId = pos.TraderId, ExchangeId = pos.ExchangeId, StrategyId = pos.StrategyId,
            PositionId = pos.Id, Pair = "BTCUSDT", Side = OrderSide.Sell, Type = OrderType.Market,
            Status = OrderStatus.Filled, FilledQuantity = 1m
        };
        posRepo.GetByIdAsync(pos.Id, Arg.Any<CancellationToken>()).Returns(pos);

        await projector.ProjectFilledAsync(order, avgFillPrice: 55000m);

        Assert.Equal(PositionStatus.Closed, pos.Status);
        Assert.Equal((55000m - 50000m) * 1m, pos.RealizedPnl);
        await posRepo.Received(1).UpdateAsync(pos, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sell_WithPositionId_Idempotent_WhenAlreadyClosed()
    {
        var (projector, posRepo, _, _) = Build();
        var pos = Position.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT", 1m, 50000m);
        pos.Close(52000m);
        var order = new Order
        {
            PositionId = pos.Id, StrategyId = pos.StrategyId, Pair = "BTCUSDT",
            Side = OrderSide.Sell, Type = OrderType.Market, Status = OrderStatus.Filled, FilledQuantity = 1m
        };
        posRepo.GetByIdAsync(pos.Id, Arg.Any<CancellationToken>()).Returns(pos);

        await projector.ProjectFilledAsync(order, avgFillPrice: 55000m);

        await posRepo.DidNotReceive().UpdateAsync(Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sell_NoPositionId_FifoClosesUntilQuantityCovered()
    {
        var (projector, posRepo, _, _) = Build();
        var strategyId = Guid.NewGuid();
        var older = Position.Open(Guid.NewGuid(), Guid.NewGuid(), strategyId, "BTCUSDT", 1m, 50000m);
        var newer = Position.Open(Guid.NewGuid(), Guid.NewGuid(), strategyId, "BTCUSDT", 1m, 51000m);
        var order = new Order
        {
            StrategyId = strategyId, Pair = "BTCUSDT", Side = OrderSide.Sell, Type = OrderType.Market,
            Status = OrderStatus.Filled, FilledQuantity = 1m // 仅覆盖最旧一笔
        };
        posRepo.GetOpenByStrategyAndPairAsync(strategyId, "BTCUSDT", Arg.Any<CancellationToken>())
            .Returns([older, newer]);

        await projector.ProjectFilledAsync(order, avgFillPrice: 55000m);

        Assert.Equal(PositionStatus.Closed, older.Status); // 平掉最旧
        Assert.Equal(PositionStatus.Open, newer.Status);   // 较新保留
        await posRepo.Received(1).UpdateAsync(Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NonTerminalOrder_NoProjection()
    {
        var (projector, posRepo, _, _) = Build();
        var order = FilledBuy(filledQty: 1m);
        order.Status = OrderStatus.Pending;

        await projector.ProjectFilledAsync(order, avgFillPrice: 100m);

        await posRepo.DidNotReceive().GetByOpeningOrderIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await posRepo.DidNotReceive().AddAsync(Arg.Any<Position>(), Arg.Any<CancellationToken>());
    }
}
