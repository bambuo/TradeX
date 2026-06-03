using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;
using TradeX.Trading.Events;
using TradeX.Trading.Outbox;
using TradeX.Trading.Streams;

namespace TradeX.Tests.Trading;

public class OutboxRelayServiceTests
{
    private static readonly MethodInfo DrainBatchMethod = typeof(OutboxRelayService)
        .GetMethod("DrainBatchAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [Fact]
    public async Task DrainBatch_PendingEvents_PublishesToRedisStream()
    {
        var (db, repo, relay) = Build();
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var pendingEvents = new List<OutboxEvent>
        {
            new()
            {
                Id = id1,
                PayloadJson = """{"Type":"OrderPlaced","TraceId":"0000-0000","TraderId":"0000-0000","DataJson":"{}"}""",
                Type = "OrderPlaced"
            },
            new()
            {
                Id = id2,
                PayloadJson = """{"Type":"PositionUpdated","TraceId":"0000-0000","TraderId":"0000-0000","DataJson":"{}"}""",
                Type = "PositionUpdated"
            }
        };
        repo.PickPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(pendingEvents);

        var processed = await InvokeDrainBatchAsync(relay, db);

        Assert.Equal(2, processed);

        // Verify each event was published to the correct Redis Stream
        foreach (var evt in pendingEvents)
        {
            await db.Received(1).StreamAddAsync(
                TradingEventChannels.Events,
                RedisStreamHelpers.PayloadField,
                Arg.Is<RedisValue>(v => v.ToString() == evt.PayloadJson),
                maxLength: RedisStreamHelpers.DefaultMaxLength,
                useApproximateMaxLength: true);
        }

        await repo.Received(1).MarkSentAsync(id1, Arg.Any<CancellationToken>());
        await repo.Received(1).MarkSentAsync(id2, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainBatch_RedisFailure_MarksFailed()
    {
        var (db, repo, relay) = Build();
        var evtId = Guid.NewGuid();
        var pendingEvents = new List<OutboxEvent>
        {
            new() { Id = evtId, PayloadJson = "{}", AttemptCount = 0, Type = "TestEvent" }
        };
        repo.PickPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(pendingEvents);

        db.When(d => d.StreamAddAsync(
                TradingEventChannels.Events,
                RedisStreamHelpers.PayloadField,
                Arg.Any<RedisValue>(),
                maxLength: Arg.Any<int?>(),
                useApproximateMaxLength: Arg.Any<bool>()))
            .Throw(new RedisServerException("Connection failed"));

        var processed = await InvokeDrainBatchAsync(relay, db);

        Assert.Equal(1, processed);
        await repo.Received(1).MarkFailedAsync(evtId,
            Arg.Is<string>(s => s.Contains("Connection failed")),
            5,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DrainBatch_NoPendingEvents_ReturnsZero()
    {
        var (db, repo, relay) = Build();
        repo.PickPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        var processed = await InvokeDrainBatchAsync(relay, db);

        Assert.Equal(0, processed);
        await db.DidNotReceiveWithAnyArgs().StreamAddAsync(default, default, default);
    }

    /// <summary>
    /// 超过 maxAttempts 后标记为 Failed，不再重试。
    /// </summary>
    [Fact]
    public async Task DrainBatch_ExceedsMaxAttempts_MarksFailed()
    {
        var (db, repo, relay) = Build();
        var evtId = Guid.NewGuid();
        var evt = new OutboxEvent
        {
            Id = evtId,
            PayloadJson = "{}",
            AttemptCount = 4, // One more failure → 5 = maxAttempts → becomes Failed
            Type = "TestEvent"
        };
        repo.PickPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([evt]);

        db.When(d => d.StreamAddAsync(
                TradingEventChannels.Events,
                RedisStreamHelpers.PayloadField,
                Arg.Any<RedisValue>(),
                maxLength: Arg.Any<int?>(),
                useApproximateMaxLength: Arg.Any<bool>()))
            .Throw(new RedisServerException("Connection failed"));

        await InvokeDrainBatchAsync(relay, db);

        // After failure with maxAttempts=5, attemptCount goes from 4→5 → Status becomes Failed
        await repo.Received(1).MarkFailedAsync(evtId,
            Arg.Any<string>(),
            Arg.Is<int>(max => max == 5),
            Arg.Any<CancellationToken>());
    }

    // ─────────────── Helpers ───────────────

    private static async Task<int> InvokeDrainBatchAsync(OutboxRelayService relay, IDatabase db)
    {
        var task = (Task<int>)DrainBatchMethod.Invoke(relay, [db, CancellationToken.None])!;
        return await task;
    }

    private static (IDatabase db, IOutboxRepository repo, OutboxRelayService relay) Build()
    {
        var db = Substitute.For<IDatabase>();
        var redis = Substitute.For<IConnectionMultiplexer>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);

        var repo = Substitute.For<IOutboxRepository>();

        var services = new ServiceCollection();
        services.AddSingleton(repo);
        var sp = services.BuildServiceProvider();
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();

        var logger = Substitute.For<ILogger<OutboxRelayService>>();
        var relay = new OutboxRelayService(scopeFactory, redis, new TradeX.Trading.Observability.TradeXMetrics(), logger);

        return (db, repo, relay);
    }
}
