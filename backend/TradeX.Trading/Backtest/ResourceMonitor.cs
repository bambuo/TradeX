using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TradeX.Trading.Backtest;

public class ResourceMonitor(
    IResourceProvider resourceProvider,
    IOptions<BacktestSchedulerSettings> settings,
    ILogger<ResourceMonitor> logger) : IHostedService, IDisposable
{
    private Timer? _timer;
    private readonly object _lock = new();
    private int _runningCount;
    private volatile int _allowedConcurrency = settings.Value.MaxConcurrency;
    private TimeSpan _previousCpuTime;
    private DateTime _previousSampleTime;
    private bool _isFirstCpuSample = true;
    private double _currentCpuPercent;
    private long _currentMemoryMb;

    public int RunningCount => _runningCount;
    public int AllowedConcurrency => _allowedConcurrency;
    public long CurrentMemoryMb => _currentMemoryMb;
    public double CurrentCpuPercent => _currentCpuPercent;

    public bool TryAcquire()
    {
        lock (_lock)
        {
            if (_runningCount >= _allowedConcurrency)
                return false;
            _runningCount++;
            return true;
        }
    }

    public void Release()
    {
        lock (_lock)
        {
            _runningCount--;
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        _previousCpuTime = resourceProvider.GetTotalProcessorTime();
        _previousSampleTime = DateTime.UtcNow;

        var intervalMs = settings.Value.MonitorIntervalSeconds * 1000;
        _timer = new Timer(SampleResources, null, intervalMs, intervalMs);

        logger.LogInformation("ResourceMonitor 启动, 监视间隔: {Interval}s", settings.Value.MonitorIntervalSeconds);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _timer?.Dispose();
        logger.LogInformation("ResourceMonitor 停止");
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private void SampleResources(object? state)
    {
        try
        {
            var memBytes = resourceProvider.GetCurrentMemoryBytes();
            _currentMemoryMb = memBytes / (1024 * 1024);

            SampleCpu();

            var memCap = CalcMemCap(_currentMemoryMb);
            var cpuCap = _isFirstCpuSample ? settings.Value.MaxConcurrency : CalcCpuCap(_currentCpuPercent);
            var newAllowed = Math.Min(memCap, cpuCap);

            if (newAllowed != _allowedConcurrency)
            {
                logger.LogInformation(
                    "资源水位变化: Memory={MemMb}MB, CPU={CpuPercent}%, AllowedConcurrency={Old}→{New}",
                    _currentMemoryMb, _currentCpuPercent, _allowedConcurrency, newAllowed);
                _allowedConcurrency = newAllowed;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ResourceMonitor 采样异常");
        }
    }

    private void SampleCpu()
    {
        var currentCpu = resourceProvider.GetTotalProcessorTime();
        var currentTime = DateTime.UtcNow;

        if (_isFirstCpuSample)
        {
            _previousCpuTime = currentCpu;
            _previousSampleTime = currentTime;
            _isFirstCpuSample = false;
            _currentCpuPercent = 0;
            return;
        }

        var cpuDelta = (currentCpu - _previousCpuTime).TotalSeconds;
        var timeDelta = (currentTime - _previousSampleTime).TotalSeconds;

        _previousCpuTime = currentCpu;
        _previousSampleTime = currentTime;

        if (timeDelta <= 0) return;

        _currentCpuPercent = Math.Round(cpuDelta / (timeDelta * resourceProvider.GetProcessorCount()) * 100, 1);
    }

    private int CalcMemCap(long memoryMb)
    {
        var s = settings.Value;
        if (memoryMb < s.MemoryWarningMb) return s.MaxConcurrency;
        if (memoryMb < s.MemoryCriticalMb) return Math.Max(1, s.MaxConcurrency - 1);
        if (memoryMb < s.MemoryAbsoluteMb) return 1;
        return 0;
    }

    private int CalcCpuCap(double cpuPercent)
    {
        var s = settings.Value;
        if (cpuPercent < s.CpuWarningPercent) return s.MaxConcurrency;
        if (cpuPercent < s.CpuCriticalPercent) return Math.Max(1, s.MaxConcurrency - 1);
        if (cpuPercent < s.CpuAbsolutePercent) return 1;
        return 0;
    }
}
