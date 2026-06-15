using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace TradeX.Trading.Rules;

/// <summary>错误级别（滑动窗口内累计错误数触发不同阈值）。</summary>
public enum ErrorLevel
{
    None,
    Warning,
    Critical,
    AutoDeactivate,
}

/// <summary>ErrorMonitor 配置。</summary>
/// <param name="WarningThreshold">警告阈值（窗口内错误数）。</param>
/// <param name="CriticalThreshold">严重阈值。</param>
/// <param name="AutoDeactivateThreshold">自动熔断阈值。</param>
/// <param name="WindowDuration">滑动窗口时长。</param>
public sealed record ErrorMonitorConfig(
    int WarningThreshold = 10,
    int CriticalThreshold = 50,
    int AutoDeactivateThreshold = 100,
    TimeSpan? WindowDuration = null
)
{
    /// <summary>返回有效的窗口时长（默认 5 分钟）。</summary>
    public TimeSpan EffectiveWindow => WindowDuration ?? TimeSpan.FromMinutes(5);
}

/// <summary>
/// 交易所级错误计数与自动熔断。
/// 基于滑动窗口统计错误数，达到不同阈值时触发警告、严重、自动熔断回调。
/// 线程安全。
/// </summary>
public sealed class ErrorMonitor
{
    private readonly record struct ErrorEntry(DateTime Time, string Message);

    private sealed class ExchangeErrors
    {
        private readonly object _lock = new();
        private readonly List<ErrorEntry> _events = [];

        public int Record(DateTime now, TimeSpan window, string message)
        {
            lock (_lock)
            {
                var cutoff = now - window;
                _events.RemoveAll(e => e.Time < cutoff);
                _events.Add(new ErrorEntry(now, message));
                return _events.Count;
            }
        }
    }

    // 内部字段：从主构造函数参数归一化而来
    private readonly ErrorMonitorConfig _config;
    private readonly ILogger<ErrorMonitor> _logger;
    private readonly Func<Guid, string, Task>? _onAutoDeactivate;
    private readonly ConcurrentDictionary<Guid, ExchangeErrors> _exchanges = new();

    /// <summary>内部时间源（可测试替换）。</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.UtcNow;

    public ErrorMonitor(
        ErrorMonitorConfig config,
        ILogger<ErrorMonitor> logger,
        Func<Guid, string, Task>? onAutoDeactivate = null)
    {
        // 归一化：无效值回退为默认
        _config = config with
        {
            WarningThreshold = config.WarningThreshold <= 0 ? 10 : config.WarningThreshold,
            CriticalThreshold = config.CriticalThreshold <= 0 ? 50 : config.CriticalThreshold,
            AutoDeactivateThreshold = config.AutoDeactivateThreshold <= 0 ? 100 : config.AutoDeactivateThreshold,
        };
        _logger = logger;
        _onAutoDeactivate = onAutoDeactivate;
    }

    /// <summary>
    /// 记录一次错误并返回当前错误级别。
    /// 当达到 AutoDeactivate 阈值时触发回调。
    /// </summary>
    public async Task<ErrorLevel> RecordAsync(Guid exchangeId, Exception error, CancellationToken ct = default)
    {
        var ee = _exchanges.GetOrAdd(exchangeId, _ => new ExchangeErrors());
        var now = Now();
        var count = ee.Record(now, _config.EffectiveWindow, error.Message);

        if (count >= _config.AutoDeactivateThreshold)
        {
            _logger.LogError(
                "ErrorMonitor: auto-deactivate threshold reached, exchangeId={ExchangeId}, errorCount={Count}, window={Window}",
                exchangeId, count, _config.EffectiveWindow);

            if (_onAutoDeactivate is not null)
            {
                await _onAutoDeactivate(exchangeId,
                    $"auto-deactivate: {count} errors in {_config.EffectiveWindow}");
            }
            return ErrorLevel.AutoDeactivate;
        }

        if (count >= _config.CriticalThreshold)
        {
            _logger.LogWarning(
                "ErrorMonitor: critical threshold reached, exchangeId={ExchangeId}, errorCount={Count}, window={Window}",
                exchangeId, count, _config.EffectiveWindow);
            return ErrorLevel.Critical;
        }

        if (count >= _config.WarningThreshold)
        {
            _logger.LogWarning(
                "ErrorMonitor: warning threshold reached, exchangeId={ExchangeId}, errorCount={Count}, window={Window}",
                exchangeId, count, _config.EffectiveWindow);
            return ErrorLevel.Warning;
        }

        return ErrorLevel.None;
    }

    /// <summary>获取指定交易所当前的滑动窗口内错误计数。</summary>
    public int GetErrorCount(Guid exchangeId)
    {
        if (!_exchanges.TryGetValue(exchangeId, out var ee))
            return 0;

        var now = Now();
        // 触发一次清理以获取准确计数
        return ee.Record(now, _config.EffectiveWindow, "__probe__") - 1; // 减去 probe 本身
    }
}
