using Microsoft.AspNetCore.SignalR;
using NSubstitute;
using TradeX.Api.Hubs;
using TradeX.Api.Services;

namespace TradeX.Tests.Services;

public class SignalREventBusTests
{
    [Fact]
    public async Task PositionUpdatedAsync_DoesNotThrow()
    {
        var bus = new SignalREventBus(Substitute.For<IHubContext<TradingHub>>());
        var exception = await Record.ExceptionAsync(() =>
            bus.PositionUpdatedAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "BTCUSDT", 1, 50000, 100, 0, "Open", DateTime.UtcNow));

        Assert.Null(exception);
    }

    [Fact]
    public async Task OrderPlacedAsync_DoesNotThrow()
    {
        var bus = new SignalREventBus(Substitute.For<IHubContext<TradingHub>>());
        var exception = await Record.ExceptionAsync(() =>
            bus.OrderPlacedAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                "BTCUSDT", "Buy", "Market", "Filled", 1, DateTime.UtcNow));

        Assert.Null(exception);
    }

    [Fact]
    public async Task RiskAlertAsync_DoesNotThrow()
    {
        var bus = new SignalREventBus(Substitute.For<IHubContext<TradingHub>>());
        var exception = await Record.ExceptionAsync(() =>
            bus.RiskAlertAsync(Guid.NewGuid(), "Warning", "DailyLoss", null, "测试告警"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DashboardSummaryAsync_DoesNotThrow()
    {
        var bus = new SignalREventBus(Substitute.For<IHubContext<TradingHub>>());
        var exception = await Record.ExceptionAsync(() =>
            bus.DashboardSummaryAsync(Guid.NewGuid(), 1000, 5, 3, 50, 60, DateTime.UtcNow));

        Assert.Null(exception);
    }

    [Fact]
    public async Task DeploymentStatusChangedAsync_DoesNotThrow()
    {
        var bus = new SignalREventBus(Substitute.For<IHubContext<TradingHub>>());
        var exception = await Record.ExceptionAsync(() =>
            bus.DeploymentStatusChangedAsync(Guid.NewGuid(), Guid.NewGuid(), "Draft", "Active", "用户启用"));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ExchangeConnectionChangedAsync_DoesNotThrow()
    {
        var bus = new SignalREventBus(Substitute.For<IHubContext<TradingHub>>());
        var exception = await Record.ExceptionAsync(() =>
            bus.ExchangeConnectionChangedAsync(Guid.NewGuid(), Guid.NewGuid(), "Connected", "Disconnected", "超时"));

        Assert.Null(exception);
    }
}
