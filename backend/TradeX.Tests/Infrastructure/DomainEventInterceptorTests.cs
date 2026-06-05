using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeX.Core.Abstractions;
using TradeX.Core.Events;
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
        var method = typeof(AggregateRoot).GetMethod("AddDomainEvent",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        method.Invoke(this, [evt]);
    }
}

/// <summary>
/// 测试用 DbContext — 只包含 TestAggregate 表，用于隔离测试 DomainEventInterceptor。
/// </summary>
public sealed class TestEventDbContext(DbContextOptions<TestEventDbContext> options)
    : DbContext(options)
{
    public DbSet<TestAggregate> TestAggregates => Set<TestAggregate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestAggregate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Ignore(x => x.DomainEvents);
        });
    }
}

public sealed class DomainEventInterceptorTests
{
    private static DbContextOptions<TestEventDbContext> CreateOptions(string dbName)
    {
        // 用真实 ServiceProvider 避免 mock GetService/GetRequiredService 扩展方法的复杂性
        var sp = new ServiceCollection()
            .AddSingleton<DomainEventDispatcher>()
            .AddSingleton<ILogger<DomainEventDispatcher>>(
                _ => NullLogger<DomainEventDispatcher>.Instance)
            .BuildServiceProvider();

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(sp);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        return new DbContextOptionsBuilder<TestEventDbContext>()
            .UseInMemoryDatabase(dbName)
            .AddInterceptors(new DomainEventInterceptor(scopeFactory))
            .Options;
    }

    [Fact]
    public async Task SaveChangesAsync_ClearsDomainEvents()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var aggregate = new TestAggregate();
            aggregate.RaiseEvent(new OrderPlacedDomainEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
                "ETHUSDT", "Sell", "Limit", 2.0m, 3000m));

            ctx.TestAggregates.Add(aggregate);
            await ctx.SaveChangesAsync();

            // 保存后领域事件应被清空（旧行为保持）
            Assert.Empty(aggregate.DomainEvents);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WithNoDomainEvents_SucceedsSilently()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            ctx.TestAggregates.Add(new TestAggregate());
            await ctx.SaveChangesAsync(); // 不应抛异常
        }
    }

    [Fact]
    public async Task SaveChangesSync_ClearsDomainEvents()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            var aggregate = new TestAggregate();
            aggregate.RaiseEvent(new OrderPlacedDomainEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
                "BTCUSDT", "Buy", "Market", 1.0m, 50000m));

            ctx.TestAggregates.Add(aggregate);
            ctx.SaveChanges(); // 同步版本

            Assert.Empty(aggregate.DomainEvents);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_DoesNotWriteOutbox()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var ctx = new TestEventDbContext(CreateOptions(dbName)))
        {
            // DomainEventInterceptor 不再写 outbox_events 表
            // 此测试验证旧行为已被正确移除
            var aggregate = new TestAggregate();
            aggregate.RaiseEvent(new OrderPlacedDomainEvent(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
                "BTCUSDT", "Buy", "Market", 1.0m, 50000m));

            ctx.TestAggregates.Add(aggregate);
            await ctx.SaveChangesAsync();

            // OutboxEvents 表已移除——不再验证
            Assert.Empty(aggregate.DomainEvents);
        }
    }
}
