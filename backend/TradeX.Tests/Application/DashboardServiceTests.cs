using NSubstitute;
using TradeX.Application.Common;
using TradeX.Application.Dashboard;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using CoreExchange = TradeX.Core.Models.Exchange;

namespace TradeX.Tests.Application;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task GetSummaryAsync_ShouldReturnCorrectSummary()
    {
        var bindingRepo = Substitute.For<IStrategyBindingRepository>();
        var positionRepo = Substitute.For<IPositionRepository>();
        var exchangeRepo = Substitute.For<IExchangeRepository>();

        // 模拟策略绑定
        var strategyBindings = new List<StrategyBinding>
        {
            StrategyBinding.Create(Guid.NewGuid(), "Strategy A", Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT", "15m", MarketType.Spot, Guid.NewGuid()),
            StrategyBinding.Create(Guid.NewGuid(), "Strategy B", Guid.NewGuid(), Guid.NewGuid(), "ETHUSDT", "15m", MarketType.Spot, Guid.NewGuid()),
        };
        bindingRepo.GetAllActiveAsync(default).Returns(strategyBindings);
        bindingRepo.GetActiveByExchangeAndPairAsync(Guid.Empty, string.Empty, default)
            .Returns([]);

        // 模拟持仓
        var positions = new List<Position>
        {
            Position.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT", 1.0m, 50000m),
            Position.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "ETHUSDT", 10.0m, 3000m),
        };
        // 设置模拟值以便断言
        positions[0].GetType().GetProperty(nameof(Position.CurrentPrice))!.SetValue(positions[0], 51000m);
        positions[0].GetType().GetProperty(nameof(Position.UnrealizedPnl))!.SetValue(positions[0], 1000m);
        positions[0].GetType().GetProperty(nameof(Position.RealizedPnl))!.SetValue(positions[0], 0m);
        positions[1].GetType().GetProperty(nameof(Position.CurrentPrice))!.SetValue(positions[1], 3100m);
        positions[1].GetType().GetProperty(nameof(Position.UnrealizedPnl))!.SetValue(positions[1], 1000m);
        positions[1].GetType().GetProperty(nameof(Position.RealizedPnl))!.SetValue(positions[1], 0m);
        positionRepo.GetAllOpenAsync(default).Returns(positions);

        // 模拟交易所
        var exchanges = new List<CoreExchange>
        {
            CoreExchange.Create(Guid.NewGuid(), "Binance", ExchangeType.Binance, "key1", "secret1"),
            CoreExchange.Create(Guid.NewGuid(), "OKX", ExchangeType.OKX, "key2", "secret2"),
        };
        exchangeRepo.GetAllEnabledAsync(default).Returns(exchanges);

        var service = new DashboardService(bindingRepo, positionRepo, exchangeRepo);
        var summary = await service.GetSummaryAsync();

        // 策略统计
        Assert.Equal(2, summary.StrategyCount);
        Assert.Equal(2, summary.ActiveStrategyCount);

        // 持仓统计
        Assert.Equal(2, summary.OpenPositionCount);
        Assert.Equal(51000m * 1.0m + 3100m * 10.0m, summary.TotalBalance);
        Assert.Equal(1000m + 0m + 1000m + 0m, summary.TotalPnl);

        // 硬编码值
        Assert.Equal(0, summary.TraderCount);
        Assert.Equal(0, summary.TodayOrderCount);
        Assert.Equal(0m, summary.WinRate);
        Assert.Empty(summary.RecentTrades);

        // 交易所状态
        Assert.Equal(2, summary.ExchangeStatus.Count);
        Assert.Contains("binance", summary.ExchangeStatus.Keys);
        Assert.Contains("okx", summary.ExchangeStatus.Keys);
        Assert.Equal("Connected", summary.ExchangeStatus["binance"]);
        Assert.Equal("Connected", summary.ExchangeStatus["okx"]);
    }

    [Fact]
    public async Task GetSummaryAsync_WithNoData_ShouldReturnDefaults()
    {
        var bindingRepo = Substitute.For<IStrategyBindingRepository>();
        var positionRepo = Substitute.For<IPositionRepository>();
        var exchangeRepo = Substitute.For<IExchangeRepository>();

        bindingRepo.GetAllActiveAsync(default).Returns([]);
        bindingRepo.GetActiveByExchangeAndPairAsync(Guid.Empty, string.Empty, default).Returns([]);
        positionRepo.GetAllOpenAsync(default).Returns([]);
        exchangeRepo.GetAllEnabledAsync(default).Returns([]);

        var service = new DashboardService(bindingRepo, positionRepo, exchangeRepo);
        var summary = await service.GetSummaryAsync();

        Assert.Equal(0, summary.StrategyCount);
        Assert.Equal(0, summary.ActiveStrategyCount);
        Assert.Equal(0, summary.OpenPositionCount);
        Assert.Equal(0m, summary.TotalBalance);
        Assert.Equal(0m, summary.TotalPnl);
        Assert.Empty(summary.ExchangeStatus);
    }
}
