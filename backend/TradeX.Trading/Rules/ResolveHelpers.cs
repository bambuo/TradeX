using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>规则链中的信号/派生值解析辅助方法。</summary>
public static class ResolveHelpers
{
    /// <summary>
    /// 解析信号或派生值。优先级：DerivedValues > Signals。
    /// </summary>
    public static (decimal Value, bool Found) Resolve(this ChainState state, string name)
    {
        if (state.DerivedValues.TryGetValue(name, out var derived))
            return (derived, true);
        if (state.Signals.TryGetValue(name, out var sig))
            return (sig.Value, true);
        return (0, false);
    }
}
