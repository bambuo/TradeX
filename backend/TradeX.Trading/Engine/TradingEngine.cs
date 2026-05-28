using System.Collections.Concurrent;

namespace TradeX.Trading.Engine;

/// <summary>
/// 引擎协调服务（非 BackgroundService）。评估逻辑已迁移到
/// <see cref="Streaming.StrategyEvaluationConsumer"/>（事件驱动）。
/// 数据流管理已迁移到 <see cref="Streaming.KlineStreamManager"/>（WebSocket 订阅）。
///
/// 保留此类是为了向后兼容引用了 <c>TradingEngine</c> 的代码（测试、DI 配置等）。
/// 此类现在是纯协调外壳，不再包含任何轮询或评估逻辑。
/// </summary>
public sealed class TradingEngine
{
    /// <summary>
    /// 波动率网格去重窗口的时间戳缓存。
    /// 由 <see cref="Streaming.KlineStreamManager"/> 持有并使用。
    /// </summary>
    public ConcurrentDictionary<string, DateTime> LastTradeTime { get; } = new();

    /// <summary>返回 TradingEngine 已成为协调服务的标识。</summary>
    public static string Status => "event-driven";
}
