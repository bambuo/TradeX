using NSubstitute;
using TradeX.Application.Common;
using TradeX.Application.Orders;
using TradeX.Application.Orders.DTOs;
using TradeX.Application.Traders;
using TradeX.Application.Traders.DTOs;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Tests.Application;

public sealed class ApplicationUseCaseTests
{
    // ─────────────── GetTradersUseCase ───────────────

    [Fact]
    public async Task GetTradersUseCase_ShouldReturnUserTraders()
    {
        var userId = Guid.NewGuid();
        var traderRepo = Substitute.For<ITraderRepository>();
        var traders = new List<Trader>
        {
            Trader.Create(userId, "Test Trader 1"),
            Trader.Create(userId, "Test Trader 2")
        };
        traderRepo.GetByUserIdAsync(userId, default).Returns(traders);

        var useCase = new GetTradersUseCase(traderRepo);
        var result = await useCase.ExecuteAsync(new GetTradersQuery(userId));

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("Test Trader 1", result.Data[0].Name);
    }

    // ─────────────── CreateTraderUseCase ───────────────

    [Fact]
    public async Task CreateTraderUseCase_ShouldCreateAndReturnDto()
    {
        var userId = Guid.NewGuid();
        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.IsNameUniqueAsync(userId, "New Trader", null, default).Returns(true);

        var useCase = new CreateTraderUseCase(traderRepo);
        var result = await useCase.ExecuteAsync(new CreateTraderCommand(userId, "New Trader"));

        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("New Trader", result.Data!.Name);
        await traderRepo.Received(1).AddAsync(Arg.Any<Trader>(), default);
    }

    [Fact]
    public async Task CreateTraderUseCase_WithDuplicateName_ShouldReturnConflict()
    {
        var userId = Guid.NewGuid();
        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.IsNameUniqueAsync(userId, "Existing", null, default).Returns(false);

        var useCase = new CreateTraderUseCase(traderRepo);
        var result = await useCase.ExecuteAsync(new CreateTraderCommand(userId, "Existing"));

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task CreateTraderUseCase_WithEmptyName_ShouldReturnBadRequest()
    {
        var userId = Guid.NewGuid();
        var traderRepo = Substitute.For<ITraderRepository>();

        var useCase = new CreateTraderUseCase(traderRepo);
        var result = await useCase.ExecuteAsync(new CreateTraderCommand(userId, ""));

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
    }

    // ─────────────── GetTraderOrdersUseCase ───────────────

    [Fact]
    public async Task GetTraderOrdersUseCase_ShouldReturnOrders()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(userId, "Test");
        // EF Core 物化时 Id 会变，需要手动设
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);

        var orderRepo = Substitute.For<IOrderRepository>();
        var orders = new List<Order>
        {
            Order.CreateManual(traderId, Guid.NewGuid(), "BTCUSDT", OrderSide.Buy, OrderType.Market, 1.0m)
        };
        orderRepo.GetByTraderIdAsync(traderId, default).Returns(orders);

        var useCase = new GetTraderOrdersUseCase(traderRepo, orderRepo);
        var result = await useCase.ExecuteAsync(new GetTraderOrdersQuery(traderId, userId));

        Assert.True(result.Success);
        Assert.Single(result.Data!);
        Assert.Equal("BTCUSDT", result.Data![0].Pair);
    }

    [Fact]
    public async Task GetTraderOrdersUseCase_WrongUser_ShouldReturnNotFound()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(Guid.NewGuid(), "Other"); // 不同 UserId

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);
        var orderRepo = Substitute.For<IOrderRepository>();

        var useCase = new GetTraderOrdersUseCase(traderRepo, orderRepo);
        var result = await useCase.ExecuteAsync(new GetTraderOrdersQuery(traderId, userId));

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    // ─────────────── CreateManualOrderUseCase ───────────────

    [Fact]
    public async Task CreateManualOrderUseCase_ShouldCreateOrder()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var exchangeId = Guid.NewGuid();
        var trader = Trader.Create(userId, "Test");
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);
        var orderRepo = Substitute.For<IOrderRepository>();

        var useCase = new CreateManualOrderUseCase(traderRepo, orderRepo);
        var result = await useCase.ExecuteAsync(new CreateManualOrderCommand(
            traderId, userId, exchangeId, "BTCUSDT", "Buy", "Limit", 1.0m, 50000m));

        Assert.True(result.Success);
        Assert.Equal(201, result.StatusCode);
        Assert.Equal("BTCUSDT", result.Data!.Pair);
        await orderRepo.Received(1).AddAsync(Arg.Any<Order>(), default);
    }

    // ─────────────── GetTraderStatsUseCase ───────────────

    [Fact]
    public async Task GetTraderStatsUseCase_ShouldComputeStats()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(userId, "Test");
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);

        var orderRepo = Substitute.For<IOrderRepository>();
        // 创建已成交的买卖订单
        var buyOrder = new Order
        {
            TraderId = traderId,
            Pair = "BTCUSDT",
            Side = OrderSide.Buy,
            Status = OrderStatus.Filled,
            Quantity = 1,
            FilledQuantity = 1,
            Price = 100m,
            QuoteQuantity = 100m
        };
        var sellOrder = new Order
        {
            TraderId = traderId,
            Pair = "BTCUSDT",
            Side = OrderSide.Sell,
            Status = OrderStatus.Filled,
            Quantity = 1,
            FilledQuantity = 1,
            Price = 150m,
            QuoteQuantity = 100m
        };
        orderRepo.GetByTraderIdAsync(traderId, default).Returns([buyOrder, sellOrder]);

        var useCase = new GetTraderStatsUseCase(traderRepo, orderRepo);
        var result = await useCase.ExecuteAsync(new GetTraderStatsQuery(traderId, userId));

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.TotalTrades);
        Assert.Equal(50m, result.Data.WinRate); // 1 win out of 2
    }
}
