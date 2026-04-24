using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Interfaces;
using TradeX.Trading;

namespace TradeX.Tests.Trading;

public class DailyLossHandlerTests
{
    [Fact]
    public async Task CheckAsync_LossUnderLimit_Passes()
    {
        var logger = Substitute.For<ILogger<DailyLossHandler>>();
        var handler = new DailyLossHandler(logger);
        var context = new RiskContext { DailyLoss = 500, MaxDailyLoss = 1000, TraderId = Guid.NewGuid() };

        var result = await handler.CheckAsync(context);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_LossOverLimit_Denies()
    {
        var logger = Substitute.For<ILogger<DailyLossHandler>>();
        var handler = new DailyLossHandler(logger);
        var context = new RiskContext { DailyLoss = 1500, MaxDailyLoss = 1000, TraderId = Guid.NewGuid() };

        var result = await handler.CheckAsync(context);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("当日亏损"));
    }
}

public class DrawdownHandlerTests
{
    [Fact]
    public async Task CheckAsync_DrawdownUnderLimit_Passes()
    {
        var logger = Substitute.For<ILogger<DrawdownHandler>>();
        var handler = new DrawdownHandler(logger);
        var context = new RiskContext { PortfolioValue = 10000, DailyLoss = 1000, MaxDrawdownPercent = 20 };

        var result = await handler.CheckAsync(context);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_DrawdownOverLimit_Denies()
    {
        var logger = Substitute.For<ILogger<DrawdownHandler>>();
        var handler = new DrawdownHandler(logger);
        var context = new RiskContext { PortfolioValue = 10000, DailyLoss = 3000, MaxDrawdownPercent = 20 };

        var result = await handler.CheckAsync(context);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("回撤"));
    }

    [Fact]
    public async Task CheckAsync_ZeroPortfolio_Passes()
    {
        var logger = Substitute.For<ILogger<DrawdownHandler>>();
        var handler = new DrawdownHandler(logger);
        var context = new RiskContext { PortfolioValue = 0, DailyLoss = 1000, MaxDrawdownPercent = 20 };

        var result = await handler.CheckAsync(context);

        Assert.True(result.IsAllowed);
    }
}

public class ConsecutiveLossHandlerTests
{
    [Fact]
    public async Task CheckAsync_LossCountUnderLimit_Passes()
    {
        var logger = Substitute.For<ILogger<ConsecutiveLossHandler>>();
        var handler = new ConsecutiveLossHandler(logger);
        var context = new RiskContext { ConsecutiveLossCount = 2, MaxConsecutiveLosses = 3 };

        var result = await handler.CheckAsync(context);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_LossCountAtLimit_Denies()
    {
        var logger = Substitute.For<ILogger<ConsecutiveLossHandler>>();
        var handler = new ConsecutiveLossHandler(logger);
        var context = new RiskContext { ConsecutiveLossCount = 3, MaxConsecutiveLosses = 3 };

        var result = await handler.CheckAsync(context);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("连续亏损"));
    }
}

public class PositionLimitHandlerTests
{
    [Fact]
    public async Task CheckAsync_PositionsUnderLimit_Passes()
    {
        var logger = Substitute.For<ILogger<PositionLimitHandler>>();
        var handler = new PositionLimitHandler(logger);
        var context = new RiskContext { OpenPositionCount = 5, MaxOpenPositions = 10 };

        var result = await handler.CheckAsync(context);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_PositionsAtLimit_Denies()
    {
        var logger = Substitute.For<ILogger<PositionLimitHandler>>();
        var handler = new PositionLimitHandler(logger);
        var context = new RiskContext { OpenPositionCount = 10, MaxOpenPositions = 10 };

        var result = await handler.CheckAsync(context);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("持仓数量"));
    }
}

public class CircuitBreakerHandlerTests
{
    [Fact]
    public async Task CheckAsync_CircuitBreakerInactive_Passes()
    {
        var logger = Substitute.For<ILogger<CircuitBreakerHandler>>();
        var handler = new CircuitBreakerHandler(logger);

        var result = await handler.CheckAsync(new RiskContext { CircuitBreakerActive = false });

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_CircuitBreakerActive_Denies()
    {
        var logger = Substitute.For<ILogger<CircuitBreakerHandler>>();
        var handler = new CircuitBreakerHandler(logger);

        var result = await handler.CheckAsync(new RiskContext { CircuitBreakerActive = true });

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("熔断"));
    }
}

public class SlippageHandlerTests
{
    [Fact]
    public async Task CheckAsync_SlippageUnderLimit_Passes()
    {
        var logger = Substitute.For<ILogger<SlippageHandler>>();
        var handler = new SlippageHandler(logger);
        var context = new RiskContext
        {
            OrderQuantity = 100,
            OrderPrice = 50,
            SlippageTolerance = 0.001m,
            MaxSlippageAmount = 10
        };

        var result = await handler.CheckAsync(context);

        Assert.True(result.IsAllowed);
    }

    [Fact]
    public async Task CheckAsync_SlippageOverLimit_Denies()
    {
        var logger = Substitute.For<ILogger<SlippageHandler>>();
        var handler = new SlippageHandler(logger);
        var context = new RiskContext
        {
            OrderQuantity = 10000,
            OrderPrice = 50,
            SlippageTolerance = 0.001m,
            MaxSlippageAmount = 10
        };

        var result = await handler.CheckAsync(context);

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("滑点"));
    }

    [Fact]
    public async Task CheckAsync_NoOrderDetails_Passes()
    {
        var logger = Substitute.For<ILogger<SlippageHandler>>();
        var handler = new SlippageHandler(logger);
        var context = new RiskContext
        {
            SlippageTolerance = 0.001m,
            MaxSlippageAmount = 10
        };

        var result = await handler.CheckAsync(context);

        Assert.True(result.IsAllowed);
    }
}

public class RiskChainIntegrationTests
{
    [Fact]
    public async Task FullChain_AllPasses_ReturnsAllowed()
    {
        var dailyLoss = new DailyLossHandler(Substitute.For<ILogger<DailyLossHandler>>());
        var drawdown = new DrawdownHandler(Substitute.For<ILogger<DrawdownHandler>>());
        var consecutiveLoss = new ConsecutiveLossHandler(Substitute.For<ILogger<ConsecutiveLossHandler>>());
        var circuitBreaker = new CircuitBreakerHandler(Substitute.For<ILogger<CircuitBreakerHandler>>());
        var positionLimit = new PositionLimitHandler(Substitute.For<ILogger<PositionLimitHandler>>());
        var slippage = new SlippageHandler(Substitute.For<ILogger<SlippageHandler>>());

        var accountRepo = Substitute.For<IExchangeAccountRepository>();
        accountRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Core.Models.ExchangeAccount
            {
                Type = Core.Enums.ExchangeType.Binance,
                ApiKeyEncrypted = "encrypted-key",
                SecretKeyEncrypted = "encrypted-secret"
            });

        var encryptionService = Substitute.For<IEncryptionService>();
        encryptionService.Decrypt("encrypted-key").Returns("decrypted-key");
        encryptionService.Decrypt("encrypted-secret").Returns("decrypted-secret");

        var exchangeClient = Substitute.For<IExchangeClient>();
        exchangeClient.TestConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(new ConnectionTestResult(true, null, "OK"));

        var clientFactory = Substitute.For<IExchangeClientFactory>();
        clientFactory.CreateClient(Arg.Any<Core.Enums.ExchangeType>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(exchangeClient);

        var exchangeHealth = new ExchangeHealthHandler(
            clientFactory, accountRepo, encryptionService,
            Substitute.For<ILogger<ExchangeHealthHandler>>());

        dailyLoss.SetNext(drawdown)
            .SetNext(consecutiveLoss)
            .SetNext(circuitBreaker)
            .SetNext(positionLimit)
            .SetNext(slippage)
            .SetNext(exchangeHealth);

        var context = new RiskContext
        {
            TraderId = Guid.NewGuid(),
            ExchangeId = Guid.NewGuid(),
            PortfolioValue = 10000,
            DailyLoss = 100,
            MaxDailyLoss = 1000,
            MaxDrawdownPercent = 20,
            ConsecutiveLossCount = 0,
            MaxConsecutiveLosses = 3,
            OpenPositionCount = 3,
            MaxOpenPositions = 10,
            OrderQuantity = 100,
            OrderPrice = 50,
            SlippageTolerance = 0.001m,
            MaxSlippageAmount = 10,
            CircuitBreakerActive = false
        };

        var result = await dailyLoss.CheckAsync(context);

        Assert.True(result.IsAllowed);
        Assert.Empty(result.DeniedReasons);
    }

    [Fact]
    public async Task FullChain_DailyLossTriggered_StopsEarly()
    {
        var dailyLoss = new DailyLossHandler(Substitute.For<ILogger<DailyLossHandler>>());
        var drawdown = new DrawdownHandler(Substitute.For<ILogger<DrawdownHandler>>());

        dailyLoss.SetNext(drawdown);

        var context = new RiskContext
        {
            DailyLoss = 2000,
            MaxDailyLoss = 1000,
            PortfolioValue = 10000,
            MaxDrawdownPercent = 20
        };

        var result = await dailyLoss.CheckAsync(context);

        Assert.False(result.IsAllowed);
        Assert.Single(result.DeniedReasons);
    }
}
