using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Interfaces;
using TradeX.Core.Models;

namespace TradeX.Core.Rules;

/// <summary>信号定义。表示一个命名指标值及其前一值。</summary>
public sealed class Signal
{
    /// <summary>信号名称（如 "RSI_14"、"MARKET_REGIME"）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>当前值。</summary>
    public decimal Value { get; set; }

    /// <summary>前一周期值（用于计算动量/背离/穿越）。</summary>
    public decimal PrevValue { get; set; }
}

/// <summary>信号上下文（输入，只读）。传递给信号管线的纯数据。</summary>
public sealed class SignalContext
{
    public string Pair { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public IReadOnlyList<Kline> KlineWindow { get; set; } = [];
    public PositionSnapshot? Position { get; set; }
    public PortfolioSnapshot? Portfolio { get; set; }
}

/// <summary>评估上下文。规则链评估所需的全部上下文数据。</summary>
public sealed class EvalContext
{
    /// <summary>交易对。</summary>
    public string Pair { get; set; } = string.Empty;

    /// <summary>交易所 ID。</summary>
    public Guid ExchangeId { get; set; }

    /// <summary>当前价格。</summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>持仓快照。</summary>
    public PositionSnapshot? Position { get; set; }

    /// <summary>组合快照。</summary>
    public PortfolioSnapshot? Portfolio { get; set; }

    /// <summary>K 线窗口。</summary>
    public IReadOnlyList<Kline> KlineWindow { get; set; } = [];

    /// <summary>评估时间（UTC）。</summary>
    public DateTime EvaluationTime { get; set; } = DateTime.UtcNow;

    /// <summary>评估作用域标识（"{bindingId}|{pair}"）。</summary>
    public string ScopeKey { get; set; } = string.Empty;

    /// <summary>有状态节点的持久化存储（可选）。为 null 时有状态节点降级为无状态模式。</summary>
    public IStateNodeStore? StateStore { get; set; }

    /// <summary>检查 KillSwitch 是否激活的可选回调。为 null 时 kill_switch 节点不做拦截。</summary>
    public Func<string, bool>? IsKillSwitchActive { get; set; }
}

/// <summary>规则链的运行时状态（请求级别，每轮评估创建一个新实例）。</summary>
public sealed class ChainState
{
    /// <summary>当前信号快照（输入，只读）。</summary>
    public Dictionary<string, Signal> Signals { get; init; } = [];

    /// <summary>评估上下文（输入，只读）。</summary>
    public EvalContext Context { get; init; } = new();

    /// <summary>Derive 节点产出的派生值（本轮、本链内有效，不跨轮不跨链）。解析优先级高于 Signals。</summary>
    public Dictionary<string, decimal> DerivedValues { get; init; } = [];

    /// <summary>Size 节点产出的仓位计算结果（Action 节点消费后不清空，但可被后续 Size 节点追加）。</summary>
    public List<SizeDecision> SizeDecisions { get; init; } = [];

    /// <summary>Action 节点产出的操作意图（单一权威载体，R-L3）。</summary>
    public List<ActionDecision> Actions { get; set; } = [];

    /// <summary>Gate 节点设为 true 时本链跳过，不参与 Coordinator 合并。</summary>
    public bool Blocked { get; set; }

    /// <summary>任何节点设为 true 时停止后续节点执行，不参与 Coordinator 合并。</summary>
    public bool Terminated { get; set; }

    /// <summary>节点执行错误记录（永不截断，用于调试和回放）。</summary>
    public List<NodeError> Errors { get; init; } = [];

    /// <summary>最终决策，仅由 ChainCoordinator 合并阶段写入（R-L3）。</summary>
    public List<StrategyDecision> Decisions { get; set; } = [];
}

/// <summary>仓位计算结果（Size 节点产出，Action 节点消费）。</summary>
public sealed class SizeDecision
{
    public string Intent { get; set; } = "ENTER";         // "ENTER" / "REDUCE" / "EXIT" / "HOLD"
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USDT";        // "USDT" / "BTC" / "PERCENT" / "CONTRACT"
    public string Reason { get; set; } = string.Empty;
}

/// <summary>操作意图（Action 节点产出，Coordinator 合并后转为 StrategyDecision）。</summary>
public sealed class ActionDecision
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Intent { get; set; } = "HOLD";          // "BUY" / "SELL" / "SELL_ALL" / "HOLD"
    public decimal Quantity { get; set; }
    public string OrderType { get; set; } = "MARKET";     // "MARKET" / "LIMIT" / "STOP_LIMIT"
    public decimal? Price { get; set; }
    public decimal? StopPrice { get; set; }
    public int Priority { get; set; }
    public string Pair { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>ChainCoordinator 合并后的最终决策输出（R-L3）。</summary>
public sealed class StrategyDecision
{
    public string Pair { get; set; } = string.Empty;
    public string Intent { get; set; } = "HOLD";          // "BUY" / "SELL" / "SELL_ALL" / "HOLD"
    public decimal Quantity { get; set; }
    public string OrderType { get; set; } = "MARKET";
    public decimal? Price { get; set; }
    public decimal? StopPrice { get; set; }
    public List<string> ActionIds { get; set; } = [];
    public string Reason { get; set; } = string.Empty;
}

/// <summary>节点执行错误记录。</summary>
public sealed class NodeError
{
    public string NodeKind { get; set; } = string.Empty;
    public RulePhase Phase { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>有状态节点的持久化数据。</summary>
public sealed class NodeState
{
    public Dictionary<string, JsonElement> Data { get; set; } = [];
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public bool IsEmpty => Data.Count == 0;
}

/// <summary>BatchWrite 的单项条目。</summary>
public sealed class StateEntry
{
    public string ScopeKey { get; set; } = string.Empty;
    public string NodeKind { get; set; } = string.Empty;
    public NodeState State { get; set; } = new();
}
