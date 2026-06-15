using System.Text.Json;
using NSubstitute;
using TradeX.Core.Enums;
using TradeX.Core.Rules;
using TradeX.Trading.Rules;

namespace TradeX.Tests.Rules;

/// <summary>ChainEngine 核心执行引擎测试（对应 Go chain_engine_test.go）。</summary>
public class ChainEngineTests
{
    // ── 测试用辅助 ────────────────────────────────────────────

    /// <summary>记录执行顺序的测试节点。</summary>
    private sealed class RecordNode(string kind, RulePhase phase, List<string> order, Exception? error, bool emitAction)
        : IRuleNode
    {
        public string Kind { get; } = kind;
        public RulePhase Phase { get; } = phase;
        public IReadOnlyList<string> Deps { get; } = [];

        public Task ProcessAsync(ChainState state, CancellationToken ct)
        {
            order.Add(Kind);
            if (emitAction)
            {
                state.Actions.Add(new ActionDecision
                {
                    Id = Kind,
                    Pair = "ETH/USDT",
                    Intent = "BUY",
                    Quantity = 1,
                    OrderType = "MARKET",
                });
            }
            if (error is not null) throw error;
            return Task.CompletedTask;
        }
    }

    private record struct Spec(string Kind, RulePhase Phase, int Priority, Exception? Error = null, bool Emit = false);

    /// <summary>用一组 Spec 构造引擎和共享执行顺序列表。</summary>
    private static (ChainEngine Engine, List<string> Order) BuildEngine(Spec[] specs)
    {
        var order = new List<string>();
        var registry = new NodeRegistry();
        var defNodes = new List<NodeInstance>();

        foreach (var s in specs)
        {
            registry.Register(s.Kind, new NodeDescriptor { Kind = s.Kind, Phase = s.Phase },
                _ => new RecordNode(s.Kind, s.Phase, order, s.Error, s.Emit));
            defNodes.Add(new NodeInstance { NodeKind = s.Kind, Priority = s.Priority });
        }

        var def = new ChainDefinition { Key = "test", Nodes = defNodes };
        var engine = new ChainEngine(def, registry);
        return (engine, order);
    }

    private static async Task<ChainState> RunAsync(ChainEngine engine)
    {
        var state = new ChainState
        {
            Signals = [],
            Context = new EvalContext { Pair = "ETH/USDT", ScopeKey = "test|ETH/USDT" },
        };
        await engine.ExecuteAsync(state);
        return state;
    }

    // ── 排序：按 Phase → Priority ─────────────────────────────

    [Fact]
    public async Task ChainEngine_SortsByPhaseThenPriority()
    {
        var (engine, order) = BuildEngine([
            new Spec("risk_b", RulePhase.Risk, 2),
            new Spec("gate_b", RulePhase.Gate, 2),
            new Spec("gate_a", RulePhase.Gate, 1),
            new Spec("risk_a", RulePhase.Risk, 1),
            new Spec("action_a", RulePhase.Action, 1),
        ]);
        await RunAsync(engine);
        Assert.Equal(["gate_a", "gate_b", "action_a", "risk_a", "risk_b"], order);
    }

    // ── Gate 错误 → Blocked ──────────────────────────────────

    [Fact]
    public async Task ChainEngine_GateErrorBlocks()
    {
        var (engine, order) = BuildEngine([
            new Spec("gate1", RulePhase.Gate, 1, new InvalidOperationException("gate boom")),
            new Spec("action1", RulePhase.Action, 1, Emit: true),
        ]);
        var state = await RunAsync(engine);

        Assert.True(state.Blocked);
        Assert.False(state.Terminated);
        Assert.Equal(["gate1"], order);
        Assert.Empty(state.Actions);
        Assert.Single(state.Errors);
    }

    // ── Action 错误 → Terminated ─────────────────────────────

    [Fact]
    public async Task ChainEngine_ActionErrorTerminates()
    {
        var (engine, order) = BuildEngine([
            new Spec("action1", RulePhase.Action, 1, new InvalidOperationException("action boom")),
            new Spec("risk1", RulePhase.Risk, 1),
        ]);
        var state = await RunAsync(engine);

        Assert.True(state.Terminated);
        Assert.Equal(["action1"], order);
    }

    // ── Override 错误 → 清空 + Terminated ─────────────────────

    [Fact]
    public async Task ChainEngine_OverrideErrorClearsAndTerminates()
    {
        var (engine, order) = BuildEngine([
            new Spec("action1", RulePhase.Action, 1, Emit: true),
            new Spec("override1", RulePhase.Override, 1, new InvalidOperationException("override boom")),
        ]);
        var state = await RunAsync(engine);

        Assert.True(state.Terminated);
        Assert.Empty(state.Actions);
        Assert.Equal(["action1", "override1"], order);
    }

    // ── Risk 错误 → 保守拒绝但继续 ────────────────────────────

    [Fact]
    public async Task ChainEngine_RiskErrorRejectsButContinues()
    {
        var (engine, order) = BuildEngine([
            new Spec("action1", RulePhase.Action, 1, Emit: true),
            new Spec("risk1", RulePhase.Risk, 1, new InvalidOperationException("risk boom")),
            new Spec("override1", RulePhase.Override, 1),
        ]);
        var state = await RunAsync(engine);

        Assert.False(state.Terminated);
        Assert.Empty(state.Actions);
        Assert.Equal(["action1", "risk1", "override1"], order);
    }

    // ── 正常路径 ──────────────────────────────────────────────

    [Fact]
    public async Task ChainEngine_HappyPath()
    {
        var (engine, _) = BuildEngine([
            new Spec("gate1", RulePhase.Gate, 1),
            new Spec("action1", RulePhase.Action, 1, Emit: true),
        ]);
        var state = await RunAsync(engine);

        Assert.False(state.Blocked);
        Assert.False(state.Terminated);
        Assert.Single(state.Actions);
        Assert.Equal("action1", state.Actions[0].Id);
    }

    // ── Blocked 后跳过后续 ────────────────────────────────────

    [Fact]
    public async Task ChainEngine_BlockedSkipsRemaining()
    {
        // Gate 不报错但手动设置 state.Blocked = true
        var order = new List<string>();
        var registry = new NodeRegistry();

        registry.Register("blocking_gate", new NodeDescriptor { Kind = "blocking_gate", Phase = RulePhase.Gate },
            _ => new BlockingGateNode(order));
        registry.Register("action1", new NodeDescriptor { Kind = "action1", Phase = RulePhase.Action },
            _ => new RecordNode("action1", RulePhase.Action, order, null, true));

        var def = new ChainDefinition
        {
            Key = "test",
            Nodes = [
                new() { NodeKind = "blocking_gate", Priority = 1 },
                new() { NodeKind = "action1", Priority = 1 },
            ],
        };
        var engine = new ChainEngine(def, registry);
        var state = await RunAsync(engine);

        Assert.True(state.Blocked);
        Assert.Equal(["blocking_gate"], order);
        Assert.Empty(state.Actions);
    }

    private sealed class BlockingGateNode(List<string> order) : IRuleNode
    {
        public string Kind => "blocking_gate";
        public RulePhase Phase => RulePhase.Gate;
        public IReadOnlyList<string> Deps { get; } = [];

        public Task ProcessAsync(ChainState state, CancellationToken ct)
        {
            order.Add(Kind);
            state.Blocked = true;
            return Task.CompletedTask;
        }
    }
}
