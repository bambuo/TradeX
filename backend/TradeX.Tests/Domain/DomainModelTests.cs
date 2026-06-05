using TradeX.Core.Enums;
using TradeX.Core.Events;
using TradeX.Core.Models;
using TradeX.Core.ValueObjects;

namespace TradeX.Tests.Domain;

public sealed class DomainModelTests
{
    // ─────────────── Order 工厂方法与领域事件 ───────────────

    [Fact]
    public void Order_CreateManual_ShouldEmitOrderPlacedEvent()
    {
        var order = Order.CreateManual(
            Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT",
            OrderSide.Buy, OrderType.Limit, 1.0m, 50000m);

        Assert.NotEqual(Guid.Empty, order.Id);
        Assert.True(order.IsManual);
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Single(order.DomainEvents);
        Assert.IsType<OrderPlacedDomainEvent>(order.DomainEvents[0]);
    }

    [Fact]
    public void Order_CreateAuto_ShouldSetMarketType()
    {
        var order = Order.CreateAuto(
            Guid.NewGuid(), Guid.NewGuid(), "ETHUSDT",
            OrderSide.Buy, 1000m, Guid.NewGuid());

        Assert.Equal(OrderType.Market, order.Type);
        Assert.Equal(1000m, order.QuoteQuantity);
        Assert.False(order.IsManual);
    }

    [Fact]
    public void Order_RecordFill_ShouldEmitOrderFilledEvent()
    {
        var order = Order.CreateManual(Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT",
            OrderSide.Buy, OrderType.Market, 1.0m);
        order.RecordFill(1.0m, 0.001m, "EX123", "BTC");

        Assert.Equal(OrderStatus.Filled, order.Status);
        Assert.Equal(2, order.DomainEvents.Count); // Created + Filled
        Assert.IsType<OrderFilledDomainEvent>(order.DomainEvents[1]);
    }

    [Fact]
    public void Order_MarkFailed_ShouldEmitOrderFailedEvent()
    {
        var order = Order.CreateManual(Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT",
            OrderSide.Buy, OrderType.Market, 1.0m);
        order.MarkFailed("余额不足");

        Assert.Equal(OrderStatus.Failed, order.Status);
        Assert.IsType<OrderFailedDomainEvent>(order.DomainEvents[1]);
    }

    [Fact]
    public void Order_MarkCancelled_ShouldEmitOrderCancelledEvent()
    {
        var order = Order.CreateManual(Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT",
            OrderSide.Buy, OrderType.Market, 1.0m);
        order.MarkCancelled();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.IsType<OrderCancelledDomainEvent>(order.DomainEvents[1]);
    }

    [Fact]
    public void Order_TerminalStatus_ShouldThrowOnFurtherMutation()
    {
        var order = Order.CreateManual(Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT",
            OrderSide.Buy, OrderType.Market, 1.0m);
        order.MarkCancelled();
        Assert.True(order.IsTerminal());

        Assert.Throws<InvalidOperationException>(() => order.RecordFill(1.0m, 0));
    }

    // ─────────────── Position 工厂方法与领域事件 ───────────────

    [Fact]
    public void Position_Open_ShouldCreateAndEmitEvent()
    {
        var pos = Position.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "BTCUSDT", 1.0m, 50000m);

        Assert.Equal(PositionStatus.Open, pos.Status);
        Assert.Single(pos.DomainEvents);
        Assert.IsType<PositionOpenedDomainEvent>(pos.DomainEvents[0]);
    }

    [Fact]
    public void Position_Close_ShouldEmitPositionClosedEvent()
    {
        var pos = Position.Open(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            "BTCUSDT", 1.0m, 50000m);
        pos.Close(55000m);

        Assert.Equal(PositionStatus.Closed, pos.Status);
        Assert.Equal(5000m, pos.RealizedPnl);
        Assert.Equal(2, pos.DomainEvents.Count);
        Assert.IsType<PositionClosedDomainEvent>(pos.DomainEvents[1]);
    }

    // ─────────────── User 领域方法 ───────────────

    [Fact]
    public void User_EnableMfa_ShouldTransitionToActive()
    {
        var user = User.Create("testuser", "test@example.com", "hash");
        Assert.Equal(UserStatus.PendingMfa, user.Status);

        user.EnableMfa("encrypted_secret", "[]");
        Assert.True(user.IsMfaEnabled);
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.Single(user.DomainEvents);
        Assert.IsType<MfaEnabledDomainEvent>(user.DomainEvents[0]);
    }

    [Fact]
    public void User_RecordLogin_ShouldEmitEvent()
    {
        var user = User.Create("testuser", "test@example.com", "hash");
        user.RecordLogin();

        Assert.NotNull(user.LastLoginAt);
        Assert.Single(user.DomainEvents);
        Assert.IsType<UserLoggedInDomainEvent>(user.DomainEvents[0]);
    }

    // ─────────────── Trader 领域方法 ───────────────

    [Fact]
    public void Trader_CreateAndDisable_ShouldEmitEvent()
    {
        var trader = Trader.Create(Guid.NewGuid(), "测试交易员");

        trader.Disable();
        Assert.Equal(TraderStatus.Disabled, trader.Status);
        Assert.Single(trader.DomainEvents);
        Assert.IsType<TraderStatusChangedDomainEvent>(trader.DomainEvents[0]);
    }

    [Fact]
    public void Trader_Activate_ShouldTransitionBack()
    {
        var trader = Trader.Create(Guid.NewGuid(), "测试交易员");
        trader.Disable();
        trader.Activate();

        Assert.Equal(TraderStatus.Active, trader.Status);
    }

    // ─────────────── Exchange 工厂方法 ───────────────

    [Fact]
    public void Exchange_Create_ShouldSetProperties()
    {
        var exchange = TradeX.Core.Models.Exchange.Create(
            Guid.NewGuid(), "Binance", ExchangeType.Binance,
            "api_key_enc", "secret_key_enc");

        Assert.Equal("Binance", exchange.Name);
        Assert.Equal(ExchangeType.Binance, exchange.Type);
        Assert.Equal(ExchangeStatus.Enabled, exchange.Status);
    }

    [Fact]
    public void Exchange_Disable_ShouldEmitEvent()
    {
        var exchange = TradeX.Core.Models.Exchange.Create(
            Guid.NewGuid(), "OKX", ExchangeType.OKX,
            "key", "secret");
        exchange.Disable();

        Assert.Equal(ExchangeStatus.Disabled, exchange.Status);
        Assert.Single(exchange.DomainEvents);
        Assert.IsType<ExchangeConnectionChangedDomainEvent>(exchange.DomainEvents[0]);
    }

    // ─────────────── StrategyBinding 领域方法 ───────────────

    [Fact]
    public void StrategyBinding_Activate_ShouldEmitEvent()
    {
        var binding = StrategyBinding.Create(
            Guid.NewGuid(), "测试策略", Guid.NewGuid(),
            Guid.NewGuid(), "BTCUSDT", "15m", Guid.NewGuid());
        binding.Activate();

        Assert.Equal(BindingStatus.Active, binding.Status);
        Assert.Single(binding.DomainEvents);
        Assert.IsType<BindingStatusChangedDomainEvent>(binding.DomainEvents[0]);
    }

    // ─────────────── BacktestTask 领域方法 ───────────────

    [Fact]
    public void BacktestTask_StartAndComplete_ShouldWork()
    {
        var task = BacktestTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), "测试策略",
            "BTCUSDT", "1h", 10000m,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, Guid.NewGuid());

        task.Start();
        Assert.Equal(BacktestTaskStatus.Running, task.Status);
        Assert.Single(task.DomainEvents);
        Assert.IsType<BacktestStartedDomainEvent>(task.DomainEvents[0]);

        task.Complete();
        Assert.Equal(BacktestTaskStatus.Completed, task.Status);
        Assert.NotNull(task.CompletedAt);
    }

    [Fact]
    public void BacktestTask_Fail_ShouldEmitEvent()
    {
        var task = BacktestTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), "测试策略",
            "BTCUSDT", "1h", 10000m,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, Guid.NewGuid());

        task.Start();
        task.Fail("数据下载失败");

        Assert.Equal(BacktestTaskStatus.Failed, task.Status);
        Assert.NotNull(task.CompletedAt);
        Assert.Equal(2, task.DomainEvents.Count); // Started + Failed
        Assert.IsType<BacktestFailedDomainEvent>(task.DomainEvents[1]);
        var failedEvent = Assert.IsType<BacktestFailedDomainEvent>(task.DomainEvents[1]);
        Assert.Equal("数据下载失败", failedEvent.Reason);
    }

    [Fact]
    public void BacktestTask_Cancel_ShouldEmitEvent()
    {
        var task = BacktestTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), "测试策略",
            "BTCUSDT", "1h", 10000m,
            DateTime.UtcNow.AddDays(-7), DateTime.UtcNow, Guid.NewGuid());

        task.Start();
        task.Cancel();

        Assert.Equal(BacktestTaskStatus.Cancelled, task.Status);
        Assert.NotNull(task.CompletedAt);
        Assert.Equal(2, task.DomainEvents.Count); // Started + Cancelled
        Assert.IsType<BacktestCancelledDomainEvent>(task.DomainEvents[1]);
    }

    [Fact]
    public void BacktestTask_Cancel_ShouldIgnoreCompleted()
    {
        var task = BacktestTask.Create(
            Guid.NewGuid(), Guid.NewGuid(), "测试",
            "BTCUSDT", "1h", 1000m,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, Guid.NewGuid());
        task.Start();
        task.Complete();
        task.Cancel(); // 终态后 Cancel 是空操作

        Assert.Equal(BacktestTaskStatus.Completed, task.Status);
    }

    // ─────────────── ConditionTree 值对象 ───────────────

    [Fact]
    public void ConditionTree_Empty_ShouldReturnFalse()
    {
        var tree = ConditionTree.FromJson("{}");
        Assert.False(tree.HasConditions);
        Assert.False(tree.Evaluate([], []));
    }

    [Fact]
    public void ConditionTree_NullJson_ShouldBeEmpty()
    {
        var tree = ConditionTree.FromJson(null!);
        Assert.False(tree.HasConditions);
    }

    [Fact]
    public void ConditionTree_SimpleComparison_ShouldEvaluate()
    {
        var tree = ConditionTree.FromJson("""{"Indicator":"RSI","Comparison":">","Value":70}""");
        Assert.True(tree.HasConditions);
        Assert.True(tree.Evaluate(new() { ["RSI"] = 75 }, []));
        Assert.False(tree.Evaluate(new() { ["RSI"] = 65 }, []));
    }

    [Fact]
    public void ConditionTree_CrossAbove_ShouldDetectCrossover()
    {
        // Ref 语义：compareValue = refVal * Value（乘数），此处 Value=1 表示取同值
        var tree = ConditionTree.FromJson("""{"Indicator":"EMA","Comparison":"CA","Value":1,"Ref":"50"}""");
        // 前值 <= 基准 && 当前 > 基准
        Assert.True(tree.Evaluate(
            new() { ["EMA"] = 65, ["50"] = 60 },
            new() { ["EMA"] = 55, ["50"] = 60 }));
        Assert.False(tree.Evaluate(
            new() { ["EMA"] = 65, ["50"] = 60 },
            new() { ["EMA"] = 65, ["50"] = 60 })); // 前值 == 基准，不触发
    }

    // ─────────────── Strategy 工厂方法与领域事件 ───────────────

    [Fact]
    public void Strategy_Create_ShouldSetProperties()
    {
        var userId = Guid.NewGuid();
        var strategy = Strategy.Create("测试策略", userId);

        Assert.NotEqual(Guid.Empty, strategy.Id);
        Assert.Equal("测试策略", strategy.Name);
        Assert.Equal(userId, strategy.CreatedBy);
        Assert.Equal(1, strategy.Version);
        Assert.Equal("{}", strategy.EntryCondition);
        Assert.Equal("{}", strategy.ExitCondition);
        Assert.Empty(strategy.DomainEvents);
    }

    [Fact]
    public void Strategy_UpdateConditions_ShouldEmitEvent()
    {
        var strategy = Strategy.Create("测试策略", Guid.NewGuid());

        strategy.UpdateConditions("RSI > 70", "RSI < 30");

        Assert.Equal("RSI > 70", strategy.EntryCondition);
        Assert.Equal("RSI < 30", strategy.ExitCondition);
        Assert.Single(strategy.DomainEvents);
        var evt = Assert.IsType<StrategyConditionsUpdatedDomainEvent>(strategy.DomainEvents[0]);
        Assert.Equal(strategy.Id, evt.StrategyId);
        Assert.Equal("RSI > 70", evt.EntryCondition);
        Assert.Equal("RSI < 30", evt.ExitCondition);
    }

    [Fact]
    public void Strategy_NewVersion_ShouldEmitEvent()
    {
        var strategy = Strategy.Create("测试策略", Guid.NewGuid());
        Assert.Equal(1, strategy.Version);

        strategy.NewVersion();

        Assert.Equal(2, strategy.Version);
        Assert.Single(strategy.DomainEvents);
        var evt = Assert.IsType<StrategyVersionCreatedDomainEvent>(strategy.DomainEvents[0]);
        Assert.Equal(strategy.Id, evt.StrategyId);
        Assert.Equal(2, evt.NewVersion);
    }

    // ─────────────── NotificationChannel 领域方法 ───────────────

    [Fact]
    public void NotificationChannel_Create_ShouldSetProperties()
    {
        var channel = NotificationChannel.Create(
            NotificationChannelType.Telegram, "交易告警", "encrypted_config");

        Assert.NotEqual(Guid.Empty, channel.Id);
        Assert.Equal(NotificationChannelType.Telegram, channel.Type);
        Assert.Equal("交易告警", channel.Name);
        Assert.Equal("encrypted_config", channel.ConfigEncrypted);
        Assert.Equal(NotificationChannelStatus.Enabled, channel.Status);
    }

    [Fact]
    public void NotificationChannel_Disable_ShouldEmitEvent()
    {
        var channel = NotificationChannel.Create(
            NotificationChannelType.Discord, "风控通知", "config_enc");
        Assert.Equal(NotificationChannelStatus.Enabled, channel.Status);

        channel.Disable();

        Assert.Equal(NotificationChannelStatus.Disabled, channel.Status);
        Assert.Single(channel.DomainEvents);
        var evt = Assert.IsType<NotificationChannelStatusChangedDomainEvent>(channel.DomainEvents[0]);
        Assert.Equal(channel.Id, evt.ChannelId);
        Assert.Equal("Enabled", evt.OldStatus);
        Assert.Equal("Disabled", evt.NewStatus);
    }

    // ─────────────── AggregateRoot 领域事件集合 ───────────────

    [Fact]
    public void AggregateRoot_ClearDomainEvents_ShouldEmpty()
    {
        var order = Order.CreateManual(Guid.NewGuid(), Guid.NewGuid(), "BTCUSDT",
            OrderSide.Buy, OrderType.Market, 1.0m);
        Assert.NotEmpty(order.DomainEvents);

        order.ClearDomainEvents();
        Assert.Empty(order.DomainEvents);
    }
}
