using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>
/// 规则链中的信号/派生值统一解析辅助方法（对应 §2.10 值命名空间与解析顺序）。
/// <para>
/// 链中存在两个值容器：ChainState.Signals（信号层产出）与 ChainState.DerivedValues
///（Derive 阶段节点产出）。节点参数里出现名字字符串时，按固定顺序查找：
/// </para>
/// <list type="number">
///   <item>先查 DerivedValues[name]（本轮链内派生值）</item>
///   <item>再查 Signals[name].Value（信号层产出）</item>
///   <item>都没有 → found=false</item>
/// </list>
/// </summary>
public static class ResolveHelpers
{
    /// <summary>
    /// 统一值解析。优先级：DerivedValues > Signals。
    /// </summary>
    public static (decimal Value, bool Found) Resolve(this ChainState state, string name)
    {
        if (state.DerivedValues.TryGetValue(name, out var derived))
            return (derived, true);
        if (state.Signals.TryGetValue(name, out var sig))
            return (sig.Value, true);
        return (0, false);
    }

    /// <summary>
    /// 解析 "ref" 类型参数引用，返回值和来源标识。
    /// 与 <see cref="Resolve"/> 功能相同，但额外返回来源（"derived" 或 "signal"），
    /// 方便节点日志和调试。
    /// </summary>
    public static (decimal Value, string Source, bool Found) ResolveRef(this ChainState state, string name)
    {
        if (state.DerivedValues.TryGetValue(name, out var derived))
            return (derived, "derived", true);
        if (state.Signals.TryGetValue(name, out var sig))
            return (sig.Value, "signal", true);
        return (0, "", false);
    }
}
