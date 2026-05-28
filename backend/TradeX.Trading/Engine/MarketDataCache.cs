using System.Collections.Concurrent;

namespace TradeX.Trading.Engine;

/// <summary>引擎运行时缓存。仅保留波动率网格去重窗口的 <see cref="LastTradeTime"/>。</summary>
public sealed class MarketDataCache
{
    public ConcurrentDictionary<string, DateTime> LastTradeTime { get; } = new();
}
