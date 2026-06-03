using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Observability;
using TradeX.Trading.Risk;

namespace TradeX.Tests.Trading;

public class KillSwitchTests
{
    private static (KillSwitch ks, IStrategyBindingRepository bindings, IOutboxRepository outbox) Build()
    {
        var bindings = Substitute.For<IStrategyBindingRepository>();
        var outbox = Substitute.For<IOutboxRepository>();
        var services = new ServiceCollection();
        services.AddSingleton(bindings);
        services.AddSingleton(outbox);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var metrics = new TradeXMetrics();
        var logger = Substitute.For<ILogger<KillSwitch>>();
        return (new KillSwitch(scopeFactory, metrics, logger), bindings, outbox);
    }

    [Fact]
    public async Task ActivateAsync_DisablesActiveBindings_AndEmitsOutbox()
    {
        var (ks, bindings, outbox) = Build();
        var actives = new List<StrategyBinding>
        {
            new() { Id = Guid.NewGuid(), Status = BindingStatus.Active },
            new() { Id = Guid.NewGuid(), Status = BindingStatus.Active }
        };
        bindings.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns(actives);

        await ks.ActivateAsync("test", Guid.NewGuid());

        Assert.True(ks.IsActive);
        Assert.All(actives, b => Assert.Equal(BindingStatus.Disabled, b.Status));
        await bindings.Received(1).UpdateRangeAsync(Arg.Is<List<StrategyBinding>>(l => l.Count == 2), Arg.Any<CancellationToken>());
        await outbox.Received(1).EnqueueAsync(
            Arg.Is<OutboxEvent>(e => e.Type == "KillSwitchActivated"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActivateAsync_AlreadyActive_NoOp()
    {
        var (ks, bindings, outbox) = Build();
        bindings.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        await ks.ActivateAsync("first", null);
        await ks.ActivateAsync("second", null);

        // 第二次激活应是 no-op, Outbox 只发了 1 条
        await outbox.Received(1).EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
        Assert.Equal("first", ks.LastReason);
    }

    [Fact]
    public async Task DeactivateAsync_AfterActivate_EmitsDeactivatedOutbox()
    {
        var (ks, bindings, outbox) = Build();
        bindings.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([]);

        await ks.ActivateAsync("incident", null);
        await ks.DeactivateAsync("resolved", Guid.NewGuid());

        Assert.False(ks.IsActive);
        await outbox.Received(1).EnqueueAsync(Arg.Is<OutboxEvent>(e => e.Type == "KillSwitchActivated"), Arg.Any<CancellationToken>());
        await outbox.Received(1).EnqueueAsync(Arg.Is<OutboxEvent>(e => e.Type == "KillSwitchDeactivated"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_WhenNotActive_NoOp()
    {
        var (ks, _, outbox) = Build();
        await ks.DeactivateAsync("nothing", Guid.NewGuid());
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DeactivateAsync_DoesNotAutoRestoreBindings()
    {
        // 解除后 binding 状态不自动恢复 Active, 必须运营人员手动重新启用
        var (ks, bindings, _) = Build();
        var b = new StrategyBinding { Id = Guid.NewGuid(), Status = BindingStatus.Active };
        bindings.GetAllActiveAsync(Arg.Any<CancellationToken>()).Returns([b]);
        await ks.ActivateAsync("test", null);
        Assert.Equal(BindingStatus.Disabled, b.Status);

        await ks.DeactivateAsync("resolved", Guid.NewGuid());

        Assert.Equal(BindingStatus.Disabled, b.Status);  // 仍是 Disabled
    }
}

public class CircuitBreakerHandlerWithKillSwitchTests
{
    [Fact]
    public async Task KillSwitchActive_PreemptsCheck_DeniesWithReason()
    {
        var ks = Substitute.For<IKillSwitch>();
        ks.IsActive.Returns(true);
        ks.LastReason.Returns("daily loss exceeded");
        var handler = new CircuitBreakerHandler(ks, Substitute.For<ILogger<CircuitBreakerHandler>>());

        var result = await handler.CheckAsync(new RiskContext { CircuitBreakerActive = false });

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("Kill Switch") && r.Contains("daily loss"));
    }

    [Fact]
    public async Task KillSwitchInactive_FallsThroughToSettingsCircuitBreaker()
    {
        var ks = Substitute.For<IKillSwitch>();
        ks.IsActive.Returns(false);
        var handler = new CircuitBreakerHandler(ks, Substitute.For<ILogger<CircuitBreakerHandler>>());

        var result = await handler.CheckAsync(new RiskContext { CircuitBreakerActive = true });

        Assert.False(result.IsAllowed);
        Assert.Contains(result.DeniedReasons, r => r.Contains("熔断"));
    }
}
