using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data;

namespace TradeX.Tests.Infrastructure;

/// <summary>
/// 测试用聚合根 — 暴露 AddDomainEvent 便于测试。
/// </summary>
public sealed class TestAggregate : AggregateRoot
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public void RaiseEvent(IDomainEvent evt)
    {
        // 通过反射调用受保护的 AddDomainEvent
        var method = typeof(AggregateRoot).GetMethod("AddDomainEvent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(this, [evt]);
    }
}

/// <summary>
/// 测试用 DbContext — 只包含 OutboxEvent 表，用于隔离测试 DomainEventInterceptor。
/// </summary>
public sealed class TestEventDbContext(DbContextOptions<TestEventDbContext> options)
    : DbContext(options)
{
    public DbSet<OutboxEvent> OutboxEvents => Set<OutboxEvent>();
    public DbSet<TestAggregate> TestAggregates => Set<TestAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestAggregate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            // EF Core 无法映射 IReadOnlyList<IDomainEvent>，这里忽略
            e.Ignore(x => x.DomainEvents);
        });

        modelBuilder.Entity<OutboxEvent>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
        });
    }
}

public sealed class DomainEventInterceptorTests
{
    private static DbContextOptions<TestEventDbContext> CreateOptions(string dbName)
        => new DbContextOptionsBuilder<TestEventDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new DomainEventInterceptor())
            .Options;

    [Fact]
    public async Task SavingChangesAsync_WithDomainEvents_WritesOutbox()
    {
        var dbName = Guid.NewGuid().ToString();
        var aggregateId = Guid.NewGuid();

        // 创建并写入聚合根 + 领域事件
        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var aggregate = new TestAggregate { Id = aggregateId };
            aggregate.RaiseEvent(new OrderPlacedEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
                "BTCUSDT", "Buy", "Market", 1.0m, 50000m));
            aggregate.RaiseEvent(new TraderStatusChangedEvent(
                Guid.NewGuid(), Guid.NewGuid(), "Active", "Disabled"));

            ctx.TestAggregates.Add(aggregate);
            await ctx.SaveChangesAsync();
        }

        // 验证 OutboxEvent 表
        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var outboxEvents = await ctx.OutboxEvents.ToListAsync();
            Assert.Equal(2, outboxEvents.Count);

            var orderPlaced = outboxEvents.First(e => e.Type == nameof(OrderPlacedEvent));
            Assert.Equal(OutboxStatus.Pending, orderPlaced.Status);

            var statusChanged = outboxEvents.First(e => e.Type == nameof(TraderStatusChangedEvent));
            Assert.Equal(OutboxStatus.Pending, statusChanged.Status);
        }
    }

    [Fact]
    public async Task SavingChangesAsync_ClearsDomainEventsAfterDispatch()
    {
        var dbName = Guid.NewGuid().ToString();
        var aggregateId = Guid.NewGuid();

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var aggregate = new TestAggregate { Id = aggregateId };
            aggregate.RaiseEvent(new OrderPlacedEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
                "ETHUSDT", "Sell", "Limit", 2.0m, 3000m));

            ctx.TestAggregates.Add(aggregate);
            await ctx.SaveChangesAsync();

            // 保存后领域事件应被清空
            Assert.Empty(aggregate.DomainEvents);
        }
    }

    [Fact]
    public async Task SavingChangesAsync_WithNoDomainEvents_DoesNotWriteOutbox()
    {
        var dbName = Guid.NewGuid().ToString();
        var aggregateId = Guid.NewGuid();

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var aggregate = new TestAggregate { Id = aggregateId };
            ctx.TestAggregates.Add(aggregate);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var outboxEvents = await ctx.OutboxEvents.ToListAsync();
            Assert.Empty(outboxEvents);
        }
    }

    [Fact]
    public async Task SavingChangesAsync_SerializesPayloadAsCamelCaseJson()
    {
        var dbName = Guid.NewGuid().ToString();
        var aggregateId = Guid.NewGuid();
        var traderId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var aggregate = new TestAggregate { Id = aggregateId };
            aggregate.RaiseEvent(new OrderPlacedEvent(
                orderId, traderId, Guid.NewGuid(), Guid.NewGuid(),
                "BTCUSDT", "Buy", "Market", 1.5m, 40000m));

            ctx.TestAggregates.Add(aggregate);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var outboxEvent = await ctx.OutboxEvents.SingleAsync();
            Assert.NotNull(outboxEvent.PayloadJson);

            // 验证 camelCase 序列化（不是 PascalCase）
            using var doc = JsonDocument.Parse(outboxEvent.PayloadJson);
            Assert.True(doc.RootElement.TryGetProperty("orderId", out _));
            Assert.True(doc.RootElement.TryGetProperty("traderId", out _));
            Assert.False(doc.RootElement.TryGetProperty("OrderId", out _));
            Assert.False(doc.RootElement.TryGetProperty("TraderId", out _));
        }
    }

    [Fact]
    public async Task SavingChangesSync_WithDomainEvents_WritesOutbox()
    {
        var dbName = Guid.NewGuid().ToString();
        var aggregateId = Guid.NewGuid();

        // 使用同步 SavingChanges 路径
        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var aggregate = new TestAggregate { Id = aggregateId };
            aggregate.RaiseEvent(new OrderPlacedEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
                "BTCUSDT", "Buy", "Market", 1.0m, 50000m));

            ctx.TestAggregates.Add(aggregate);
            ctx.SaveChanges(); // 同步版本
        }

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var count = await ctx.OutboxEvents.CountAsync();
            Assert.Equal(1, count);
        }
    }
}
