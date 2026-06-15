using Microsoft.Extensions.Logging;
using NSubstitute;
using TradeX.Trading.Rules;

namespace TradeX.Tests.Rules;

/// <summary>ErrorMonitor 交易所级错误监控测试。</summary>
public class ErrorMonitorTests
{
    private static ILogger<ErrorMonitor> CreateLogger() =>
        Substitute.For<ILogger<ErrorMonitor>>();

    // ── 基础阈值 ──────────────────────────────────────────────

    [Fact]
    public async Task RecordAsync_BelowThreshold_ReturnsNone()
    {
        var monitor = new ErrorMonitor(
            new ErrorMonitorConfig(WarningThreshold: 5), CreateLogger());

        var level = await monitor.RecordAsync(Guid.NewGuid(), new Exception("err"));
        Assert.Equal(ErrorLevel.None, level);
    }

    [Fact]
    public async Task RecordAsync_ReachesWarning_ReturnsWarning()
    {
        var monitor = new ErrorMonitor(
            new ErrorMonitorConfig(WarningThreshold: 3), CreateLogger());
        var exchangeId = Guid.NewGuid();

        for (var i = 0; i < 2; i++)
            await monitor.RecordAsync(exchangeId, new Exception("err"));

        var level = await monitor.RecordAsync(exchangeId, new Exception("err"));
        Assert.Equal(ErrorLevel.Warning, level);
    }

    [Fact]
    public async Task RecordAsync_ReachesCritical_ReturnsCritical()
    {
        var monitor = new ErrorMonitor(
            new ErrorMonitorConfig(WarningThreshold: 2, CriticalThreshold: 3), CreateLogger());
        var exchangeId = Guid.NewGuid();

        for (var i = 0; i < 2; i++)
            await monitor.RecordAsync(exchangeId, new Exception("err"));

        var level = await monitor.RecordAsync(exchangeId, new Exception("err"));
        Assert.Equal(ErrorLevel.Critical, level);
    }

    [Fact]
    public async Task RecordAsync_ReachesAutoDeactivate_ReturnsAutoDeactivate()
    {
        var callbackInvoked = false;
        var monitor = new ErrorMonitor(
            new ErrorMonitorConfig(
                WarningThreshold: 2,
                CriticalThreshold: 3,
                AutoDeactivateThreshold: 4),
            CreateLogger(),
            onAutoDeactivate: (_, _) =>
            {
                callbackInvoked = true;
                return Task.CompletedTask;
            });
        var exchangeId = Guid.NewGuid();

        for (var i = 0; i < 3; i++)
            await monitor.RecordAsync(exchangeId, new Exception("err"));

        var level = await monitor.RecordAsync(exchangeId, new Exception("err"));
        Assert.Equal(ErrorLevel.AutoDeactivate, level);
        Assert.True(callbackInvoked);
    }

    // ── 交易所隔离 ────────────────────────────────────────────

    [Fact]
    public async Task RecordAsync_DifferentExchanges_Independent()
    {
        var monitor = new ErrorMonitor(
            new ErrorMonitorConfig(WarningThreshold: 2), CreateLogger());
        var exA = Guid.NewGuid();
        var exB = Guid.NewGuid();

        await monitor.RecordAsync(exA, new Exception("err"));
        await monitor.RecordAsync(exA, new Exception("err"));
        var levelA = await monitor.RecordAsync(exA, new Exception("err"));

        var levelB = await monitor.RecordAsync(exB, new Exception("err"));

        Assert.Equal(ErrorLevel.Warning, levelA);
        Assert.Equal(ErrorLevel.None, levelB);
    }

    // ── 配置归一化 ────────────────────────────────────────────

    [Fact]
    public async Task ErrorMonitor_NormalizesInvalidConfig()
    {
        // 无效值（0 和负数）应回退为默认 Warning=10
        var monitor = new ErrorMonitor(
            new ErrorMonitorConfig(WarningThreshold: 0, CriticalThreshold: -1), CreateLogger());
        var exchangeId = Guid.NewGuid();

        // 默认 Warning=10，所以第 9 次应该还是 None
        for (var i = 0; i < 8; i++)
            await monitor.RecordAsync(exchangeId, new Exception("err"));

        var level = await monitor.RecordAsync(exchangeId, new Exception("err"));
        Assert.Equal(ErrorLevel.None, level);

        // 但第 10 次应触发 Warning
        level = await monitor.RecordAsync(exchangeId, new Exception("err"));
        Assert.Equal(ErrorLevel.Warning, level);
    }

    // ── GetErrorCount ─────────────────────────────────────────

    [Fact]
    public async Task GetErrorCount_ReturnsCorrectCount()
    {
        var monitor = new ErrorMonitor(
            new ErrorMonitorConfig(), CreateLogger());
        var exchangeId = Guid.NewGuid();

        for (var i = 0; i < 5; i++)
            await monitor.RecordAsync(exchangeId, new Exception("err"));

        var count = monitor.GetErrorCount(exchangeId);
        Assert.Equal(5, count);
    }

    [Fact]
    public void GetErrorCount_UnknownExchange_ReturnsZero()
    {
        var monitor = new ErrorMonitor(new ErrorMonitorConfig(), CreateLogger());
        Assert.Equal(0, monitor.GetErrorCount(Guid.NewGuid()));
    }
}
