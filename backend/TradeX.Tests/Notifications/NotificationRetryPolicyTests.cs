using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Core.Interfaces;
using TradeX.Notifications;

namespace TradeX.Tests.Notifications;

public class NotificationRetryPolicyTests
{
    private static NotificationRetryPolicy Build(INotificationMetrics? metrics = null)
    {
        metrics ??= new NullNotificationMetrics();
        return new NotificationRetryPolicy(metrics, Substitute.For<ILogger<NotificationRetryPolicy>>());
    }

    private static NotificationEvent SampleEvent() => new("test", "demo-strategy", new() { ["k"] = "v" });

    [Fact]
    public async Task ExecuteAsync_FirstAttemptSucceeds()
    {
        var policy = Build();
        var ok = await policy.ExecuteAsync("telegram", SampleEvent(), _ => Task.CompletedTask, CancellationToken.None);
        Assert.True(ok);
    }

    [Fact]
    public async Task ExecuteAsync_TransientFailureThenSuccess_Succeeds()
    {
        var policy = Build();
        var attempts = 0;

        var ok = await policy.ExecuteAsync("discord", SampleEvent(), _ =>
        {
            attempts++;
            if (attempts < 2) throw new HttpRequestException("transient");
            return Task.CompletedTask;
        }, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ExecuteAsync_AllAttemptsFail_ReturnsFalse()
    {
        var metrics = Substitute.For<INotificationMetrics>();
        var policy = Build(metrics);
        var attempts = 0;

        var ok = await policy.ExecuteAsync("telegram", SampleEvent(), _ =>
        {
            attempts++;
            throw new HttpRequestException("network down");
        }, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(NotificationRetryPolicy.MaxAttempts, attempts);
        metrics.Received(1).RecordFailed("telegram");
    }

    [Fact]
    public async Task ExecuteAsync_ConfigError_FailsFastNoRetry()
    {
        var metrics = Substitute.For<INotificationMetrics>();
        var policy = Build(metrics);
        var attempts = 0;

        var ok = await policy.ExecuteAsync("email", SampleEvent(), _ =>
        {
            attempts++;
            throw new InvalidOperationException("Email To 未配置");
        }, CancellationToken.None);

        Assert.False(ok);
        Assert.Equal(1, attempts);
        metrics.Received(1).RecordFailed("email");
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
