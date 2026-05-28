namespace TradeX.Indicators;

/// <summary>
/// 一根 K 线窗口的输入: 收盘价序列, 成交量序列, 以及 OHLC 末根的拆解.
/// 由 BacktestEngine 和实盘 TradingEngine 共同填充, 指标函数据此计算单值.
/// </summary>
public readonly record struct KlineWindow(
    IReadOnlyList<decimal> Prices,
    IReadOnlyList<long> Volumes,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close);

public delegate decimal IndicatorCompute(KlineWindow window);

/// <summary>
/// 指标注册中心 — 新增指标 = 实现一个 IndicatorCompute + 调用 Register.
/// 引擎不再硬编码任何指标名, 仅遍历已注册条目按需计算.
/// </summary>
public interface IIndicatorRegistry
{
    /// <summary>注册一个指标计算器, 同名会覆盖.</summary>
    void Register(string name, IndicatorCompute compute);

    /// <summary>计算所有已注册指标在给定窗口下的值. 返回 name → 值 字典.</summary>
    Dictionary<string, decimal> ComputeAll(KlineWindow window);

    /// <summary>计算指定子集指标. 未注册名跳过.</summary>
    Dictionary<string, decimal> Compute(IEnumerable<string> names, KlineWindow window);

    /// <summary>当前已注册指标名集合, 调试和 schema 校验用.</summary>
    IReadOnlyCollection<string> RegisteredNames { get; }
}
