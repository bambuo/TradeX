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

    // ─────────────── GetTraderByIdUseCase ───────────────

    [Fact]
    public async Task GetTraderByIdUseCase_ShouldReturnTrader()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(userId, "Test Trader");
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);

        var useCase = new GetTraderByIdUseCase(traderRepo);
        var result = await useCase.ExecuteAsync(new GetTraderByIdQuery(traderId, userId));

        Assert.True(result.Success);
        Assert.Equal("Test Trader", result.Data!.Name);
        Assert.Equal("Active", result.Data.Status);
    }

    [Fact]
    public async Task GetTraderByIdUseCase_WrongUser_ShouldReturnNotFound()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(Guid.NewGuid(), "Other"); // 不同的 UserId

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);

        var useCase = new GetTraderByIdUseCase(traderRepo);
        var result = await useCase.ExecuteAsync(new GetTraderByIdQuery(traderId, userId));

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task GetTraderByIdUseCase_NonExistent_ShouldReturnNotFound()
    {
        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(Arg.Any<Guid>(), default).Returns((Trader?)null);

        var useCase = new GetTraderByIdUseCase(traderRepo);
        var result = await useCase.ExecuteAsync(new GetTraderByIdQuery(Guid.NewGuid(), Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    // ─────────────── UpdateTraderUseCase ───────────────

    [Fact]
    public async Task UpdateTraderUseCase_ShouldUpdateAndReturn()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(userId, "Original Name");
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);
        traderRepo.IsNameUniqueAsync(userId, "Updated Name", traderId, default).Returns(true);

        var bindingRepo = Substitute.For<IStrategyBindingRepository>();

        var useCase = new UpdateTraderUseCase(traderRepo, bindingRepo);
        var result = await useCase.ExecuteAsync(new UpdateTraderCommand(
            traderId, userId, Name: "Updated Name", Status: TraderStatus.Active,
            AvatarColor: "#ff0000", Style: "dark"));

        Assert.True(result.Success);
        Assert.Equal("Updated Name", result.Data!.Name);
        Assert.Equal("Active", result.Data.Status);
        Assert.Equal("#ff0000", result.Data.AvatarColor);
        Assert.Equal("dark", result.Data.Style);
        await traderRepo.Received(1).UpdateAsync(Arg.Is<Trader>(t => t.Name == "Updated Name"), default);
    }

    [Fact]
    public async Task UpdateTraderUseCase_WrongUser_ShouldReturnNotFound()
    {
        var trader = Trader.Create(Guid.NewGuid(), "Other");
        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(Arg.Any<Guid>(), default).Returns(trader);
        var bindingRepo = Substitute.For<IStrategyBindingRepository>();

        var useCase = new UpdateTraderUseCase(traderRepo, bindingRepo);
        var result = await useCase.ExecuteAsync(new UpdateTraderCommand(
            Guid.NewGuid(), Guid.NewGuid(), Name: "New Name"));

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task UpdateTraderUseCase_DuplicateName_ShouldReturnConflict()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(userId, "Original");
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);
        traderRepo.IsNameUniqueAsync(userId, "Duplicate", traderId, default).Returns(false);
        var bindingRepo = Substitute.For<IStrategyBindingRepository>();

        var useCase = new UpdateTraderUseCase(traderRepo, bindingRepo);
        var result = await useCase.ExecuteAsync(new UpdateTraderCommand(
            traderId, userId, Name: "Duplicate"));

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
    }

    [Fact]
    public async Task UpdateTraderUseCase_DisableTrader_ShouldDeactivateBindings()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(userId, "Active Trader");
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var binding1 = StrategyBinding.Create(Guid.NewGuid(), "B1", traderId, Guid.NewGuid(), "BTCUSDT", "15m", userId);
        binding1.Activate(); // 设为 Active
        var binding2 = StrategyBinding.Create(Guid.NewGuid(), "B2", traderId, Guid.NewGuid(), "ETHUSDT", "15m", userId);
        // binding2 默认 Disabled

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);
        traderRepo.IsNameUniqueAsync(userId, Arg.Any<string>(), traderId, default).Returns(true);

        var bindingRepo = Substitute.For<IStrategyBindingRepository>();
        bindingRepo.GetByTraderIdAsync(traderId, default).Returns([binding1, binding2]);

        var useCase = new UpdateTraderUseCase(traderRepo, bindingRepo);
        var result = await useCase.ExecuteAsync(new UpdateTraderCommand(
            traderId, userId, Status: TraderStatus.Disabled));

        Assert.True(result.Success);
        Assert.Equal("Disabled", result.Data!.Status);

        // binding1 应该是 Active→Disabled，binding2 保持 Disabled
        Assert.Equal(BindingStatus.Disabled, binding1.Status);
        Assert.Equal(BindingStatus.Disabled, binding2.Status);
        await bindingRepo.Received(1).UpdateAsync(binding1, default);
        // binding2 不需要更新，因为它已经是 Disabled
    }

    // ─────────────── DeleteTraderUseCase ───────────────

    [Fact]
    public async Task DeleteTraderUseCase_ShouldDelete()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(userId, "To Delete");
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);

        var bindingRepo = Substitute.For<IStrategyBindingRepository>();
        bindingRepo.GetByTraderIdAsync(traderId, default).Returns([]);

        var useCase = new DeleteTraderUseCase(traderRepo, bindingRepo);
        var result = await useCase.ExecuteAsync(new DeleteTraderCommand(traderId, userId));

        Assert.True(result.Success);
        Assert.Equal(204, result.StatusCode);
        await traderRepo.Received(1).DeleteAsync(trader, default);
    }

    [Fact]
    public async Task DeleteTraderUseCase_WrongUser_ShouldReturnNotFound()
    {
        var trader = Trader.Create(Guid.NewGuid(), "Other");
        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(Arg.Any<Guid>(), default).Returns(trader);
        var bindingRepo = Substitute.For<IStrategyBindingRepository>();

        var useCase = new DeleteTraderUseCase(traderRepo, bindingRepo);
        var result = await useCase.ExecuteAsync(new DeleteTraderCommand(Guid.NewGuid(), Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
    }

    [Fact]
    public async Task DeleteTraderUseCase_WithActiveBindings_ShouldReturnConflict()
    {
        var userId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var trader = Trader.Create(userId, "Has Active");
        var traderField = typeof(Trader).GetProperty(nameof(Trader.Id))!;
        traderField.SetValue(trader, traderId);

        var binding = StrategyBinding.Create(Guid.NewGuid(), "Active Binding", traderId, Guid.NewGuid(), "BTCUSDT", "15m", userId);
        binding.Activate();

        var traderRepo = Substitute.For<ITraderRepository>();
        traderRepo.GetByIdAsync(traderId, default).Returns(trader);

        var bindingRepo = Substitute.For<IStrategyBindingRepository>();
        bindingRepo.GetByTraderIdAsync(traderId, default).Returns([binding]);

        var useCase = new DeleteTraderUseCase(traderRepo, bindingRepo);
        var result = await useCase.ExecuteAsync(new DeleteTraderCommand(traderId, userId));

        Assert.False(result.Success);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("活跃策略", result.Error);
        await traderRepo.DidNotReceive().DeleteAsync(Arg.Any<Trader>(), default);
    }
}
