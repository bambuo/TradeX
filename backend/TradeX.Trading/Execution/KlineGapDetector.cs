namespace TradeX.Trading.Execution;

/// <summary>
/// K 线缺口检测. 项目当前用 15s 轮询 (TradingEngine) 而非 WS, 但仍可能出现:
///   1) REST 拉取连续失败 → 缓存最后一根明显落后于"应有最新根"
///   2) 系统重启或网络中断恢复 → 需要回填缺口
/// 该组件不发起网络调用, 仅根据 (lastSeenUtc, nowUtc, intervalSeconds) 给出需要补抓的窗口列表.
/// TradingEngine 或专门的 recovery service 拿到窗口后调 IExchangeClient.GetKlinesAsync.
/// </summary>
public sealed class KlineGapDetector
{
    /// <summary>
    /// 计算需要补抓的 [start, end] 窗口. 通常返回 0 或 1 个窗口;
    /// 当 lastSeenUtc 在 nowUtc 之前 >= 2 个 K 线周期时, 返回一个 (lastSeenUtc + interval, nowUtc] 窗口.
    /// </summary>
    public IReadOnlyList<KlineGap> DetectGaps(DateTime lastSeenUtc, DateTime nowUtc, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero) return [];
        if (lastSeenUtc >= nowUtc) return [];

        var elapsed = nowUtc - lastSeenUtc;
        // 容忍 1 个周期的延迟; 缺口至少跨过 2 个完整周期才补抓
        if (elapsed < interval * 2) return [];

        return [new KlineGap(lastSeenUtc + interval, nowUtc)];
    }

    /// <summary>常用 timeframe → TimeSpan 映射.</summary>
    public static TimeSpan IntervalFromTimeframe(string timeframe) => timeframe switch
    {
        "1m" => TimeSpan.FromMinutes(1),
        "5m" => TimeSpan.FromMinutes(5),
        "15m" => TimeSpan.FromMinutes(15),
        "30m" => TimeSpan.FromMinutes(30),
        "1h" => TimeSpan.FromHours(1),
        "4h" => TimeSpan.FromHours(4),
        "1d" => TimeSpan.FromDays(1),
        _ => throw new ArgumentException($"不支持的周期: {timeframe}")
    };
}

public sealed record KlineGap(DateTime StartAt, DateTime EndAt)
{
    public TimeSpan Duration => EndAt - StartAt;
}
