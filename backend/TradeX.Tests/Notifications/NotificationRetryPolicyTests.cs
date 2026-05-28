using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Notifications;

namespace TradeX.Tests.Notifications;

public class NotificationRetryPolicyTests
{
    private static NotificationRetryPolicy Build(IOutboxRepository? outbox = null, INotificationMetrics? metrics = null)
    {
        outbox ??= Substitute.For<IOutboxRepository>();
        metrics ??= new NullNotificationMetrics();
        return new NotificationRetryPolicy(outbox, metrics, Substitute.For<ILogger<NotificationRetryPolicy>>());
    }

    private static NotificationEvent SampleEvent() => new("test", "demo-strategy", new() { ["k"] = "v" });

    [Fact]
    public async Task ExecuteAsync_FirstAttemptSucceeds_NoDeadLetter()
    {
        var outbox = Substitute.For<IOutboxRepository>();
        var policy = Build(outbox);

        var ok = await policy.ExecuteAsync("telegram", SampleEvent(), _ => Task.CompletedTask, CancellationToken.None);

        Assert.True(ok);
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailureThenSuccess_Succeeds()
    {
        var outbox = Substitute.For<IOutboxRepository>();
        var policy = Build(outbox);
        var attempts = 0;

        var ok = await policy.ExecuteAsync("discord", SampleEvent(), _ =>
        {
            attempts++;
            if (attempts < 2) throw new HttpRequestException("transient");
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(2, attempts);
        await outbox.DidNotReceive().EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AllAttemptsFail_WritesDeadLetter()
    {
        var outbox = Substitute.For<IOutboxRepository>();
        var policy = Build(outbox);
        var attempts = 0;

        var ok = await policy.ExecuteAsync("telegram", SampleEvent(), _ =>
        {
            attempts++;
            throw new HttpRequestException("network down");
        }, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(NotificationRetryPolicy.MaxAttempts, attempts);
        await outbox.Received(1).EnqueueAsync(
            Arg.Is<OutboxEvent>(e => e.Type == "NotificationFailed" && e.PayloadJson.Contains("telegram") && e.PayloadJson.Contains("network down")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ConfigError_FailsFastNoRetry()
    {
        var outbox = Substitute.For<IOutboxRepository>();
        var policy = Build(outbox);
        var attempts = 0;

        var ok = await policy.ExecuteAsync("email", SampleEvent(), _ =>
        {
            attempts++;
            throw new InvalidOperationException("Email To 未配置");
        }, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(1, attempts);
        await outbox.Received(1).EnqueueAsync(Arg.Any<OutboxEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_CancellationToken_Propagates()
    {
        var policy = Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            policy.ExecuteAsync("telegram", SampleEvent(), token =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }, cts.Token));
    }
}
