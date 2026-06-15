namespace TradeX.Core.Enums;

/// <summary>
/// 规则链节点的执行阶段，决定执行顺序。
/// 数值越小越先执行：Gate(0) → Filter(1) → Derive(2) → Size(3) → Action(4) → Risk(5) → Override(6)。
/// </summary>
public enum RulePhase
{
    /// <summary>条件门：是否执行这条链。设置 Blocked=true 跳过整链。</summary>
    Gate = 0,

    /// <summary>信号过滤/转换（如最小名义值、滑点过滤）。</summary>
    Filter = 1,

    /// <summary>信号衍生新值（写入 DerivedValues，如 crossover_check、atr_stop_calc）。</summary>
    Derive = 2,

    /// <summary>仓位计算（产出 SizeDecision，如 fixed_size、pyramiding_size）。</summary>
    Size = 3,

    /// <summary>决策生成（追加 ActionDecision，如 signal_action、grid_action）。</summary>
    Action = 4,

    /// <summary>风控检查（过滤/缩放 Actions，如 max_position_size、cooldown）。</summary>
    Risk = 5,

    /// <summary>覆盖/熔断（清空 Actions + Terminated，如 kill_switch、emergency_exit）。</summary>
    Override = 6,
}
