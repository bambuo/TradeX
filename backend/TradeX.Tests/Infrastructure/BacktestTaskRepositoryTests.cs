using Microsoft.EntityFrameworkCore;
using TradeX.Core.Models;
using TradeX.Infrastructure.Data;
using TradeX.Infrastructure.Data.Repositories;

namespace TradeX.Tests.Infrastructure;

/// <summary>
/// 回归：全局 NoTracking 下，BacktestScheduler 在同一 scope 内对同一任务多次 UpdateAsync
/// （阶段推进 + 写结果/收尾）。修复前第二次 Update 会抛
/// "The instance of entity type 'BacktestTask' cannot be tracked..."，导致每个回测任务必失败。
/// </summary>
public class BacktestTaskRepositoryTests
{
    private static DbContextOptions<TradeXDbContext> NoTrackingOptions(string dbName)
        => new DbContextOptionsBuilder<TradeXDbContext>()
            .UseInMemoryDatabase(dbName)
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking) // 与生产一致
            .Options;

    [Fact]
    public async Task UpdateAsync_MultipleTimesSameScope_DoesNotThrowTrackingConflict()
    {
        var dbName = Guid.NewGuid().ToString();
        var taskId = Guid.NewGuid();

        // 由独立 scope 写入任务
        await using (var seedCtx = new TradeXDbContext(NoTrackingOptions(dbName)))
        {
            await new BacktestTaskRepository(seedCtx).AddAsync(new BacktestTask
            {
                Id = taskId,
                StrategyId = Guid.NewGuid(),
                ExchangeId = Guid.NewGuid(),
                StrategyName = "T",
                Pair = "BTCUSDT",
                Timeframe = "1h",
                Status = BacktestTaskStatus.Pending
            });
        }

        // 模拟 ProcessTaskAsync 的单一 scope：多次 GetById + Update（每次都是新实例）
        await using var ctx = new TradeXDbContext(NoTrackingOptions(dbName));
        var repo = new BacktestTaskRepository(ctx);

        await AdvanceAsync(repo, taskId, BacktestPhase.Queued);
        await AdvanceAsync(repo, taskId, BacktestPhase.FetchingData); // 修复前在此抛异常
        await AdvanceAsync(repo, taskId, BacktestPhase.Running);

        var final = await repo.GetByIdAsync(taskId);
        Assert.NotNull(final);
        Assert.Equal(BacktestTaskStatus.Running, final!.Status);
        Assert.Equal(BacktestPhase.Running, final.Phase);
    }

    private static async Task AdvanceAsync(BacktestTaskRepository repo, Guid taskId, BacktestPhase phase)
    {
        var task = await repo.GetByIdAsync(taskId);
        Assert.NotNull(task);
        task!.Status = BacktestTaskStatus.Running;
        task.Phase = phase;
        await repo.UpdateAsync(task);
    }
}
