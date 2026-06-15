using System.Text.Json;
using TradeX.Core.Enums;
using TradeX.Core.Rules;

namespace TradeX.Tests.Rules;

/// <summary>ChainValidator 静态校验器测试（对应 Go validator_test.go）。</summary>
public class ChainValidatorTests
{
    // ── 辅助方法 ──────────────────────────────────────────────

    private static NodeInstance Ni(string kind, string json) =>
        new() { NodeKind = kind, Params = JsonSerializer.Deserialize<JsonElement>(json) };

    private static bool HasErrField(List<ValidationError> errs, string field) =>
        errs.Any(e => e.Field == field);

    private static ChainValidatorConfig DefaultConfig(
        IReadOnlySet<string>? kinds = null,
        IReadOnlySet<string>? signals = null,
        IReadOnlyDictionary<string, RulePhase>? phases = null) => new(
        RegisteredKinds: kinds ?? new HashSet<string>(),
        RegisteredSignalNames: signals ?? new HashSet<string>(),
        RegisteredEmitNames: new HashSet<string>(),
        NodePhases: phases ?? new Dictionary<string, RulePhase>(),
        AllowDuplicateKinds: new HashSet<string>(),
        RefParamNames: new Dictionary<string, IReadOnlyList<string>>(),
        ActionProducerKinds: new HashSet<string>()
    );

    // ── 校验项 1：节点 Kind 存在性 ────────────────────────────

    [Fact]
    public void ValidateChains_UnknownKind_ShouldReject()
    {
        var cfg = DefaultConfig();
        var chain = new ChainDefinition { Key = "u", Nodes = [Ni("no_such_node", "{}")] };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.True(HasErrField(errs, "nodeKind"));
    }

    [Fact]
    public void ValidateChains_RegisteredKind_ShouldPass()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["signal_action"]),
            phases: new Dictionary<string, RulePhase> { ["signal_action"] = RulePhase.Action });
        var chain = new ChainDefinition { Key = "ok", Nodes = [Ni("signal_action", "{}")] };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.False(HasErrField(errs, "nodeKind"));
    }

    // ── 校验项 4：阶段完整性 ──────────────────────────────────

    [Fact]
    public void ValidateChains_MissingActionOrOverride_ShouldReject()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["regime_gate"]),
            phases: new Dictionary<string, RulePhase> { ["regime_gate"] = RulePhase.Gate });
        var chain = new ChainDefinition { Key = "m", Nodes = [Ni("regime_gate", "{}")] };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.True(HasErrField(errs, "nodes"));
    }

    // ── 校验项 7：最坏敞口 ────────────────────────────────────

    [Fact]
    public void CheckWorstExposure_MartingaleExceeds_ShouldReject()
    {
        // 20×(3^10−1)/2 = 590480 ≫ 3000
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["martingale_action", "max_position_size"]),
            phases: new Dictionary<string, RulePhase>
            {
                ["martingale_action"] = RulePhase.Action,
                ["max_position_size"] = RulePhase.Risk,
            });
        var chain = new ChainDefinition
        {
            Key = "m",
            Nodes = [
                Ni("martingale_action", """{"baseAmount":20,"multiplier":3,"maxLevels":10}"""),
                Ni("max_position_size", """{"maxNotional":3000}"""),
            ],
        };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.True(HasErrField(errs, "params.worstExposure"));
    }

    [Fact]
    public void CheckWorstExposure_MartingaleWithinBounds_ShouldPass()
    {
        // 20×(2^6−1) = 1260 ≤ 3000
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["martingale_action", "max_position_size"]),
            phases: new Dictionary<string, RulePhase>
            {
                ["martingale_action"] = RulePhase.Action,
                ["max_position_size"] = RulePhase.Risk,
            });
        var chain = new ChainDefinition
        {
            Key = "m",
            Nodes = [
                Ni("martingale_action", """{"baseAmount":20,"multiplier":2,"maxLevels":6}"""),
                Ni("max_position_size", """{"maxNotional":3000}"""),
            ],
        };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.False(HasErrField(errs, "params.worstExposure"));
    }

    [Fact]
    public void CheckWorstExposure_OverflowGuard_ShouldReport()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["martingale_action", "max_position_size"]),
            phases: new Dictionary<string, RulePhase>
            {
                ["martingale_action"] = RulePhase.Action,
                ["max_position_size"] = RulePhase.Risk,
            });
        var chain = new ChainDefinition
        {
            Key = "m",
            Nodes = [
                Ni("martingale_action", """{"baseAmount":20,"multiplier":2,"maxLevels":150}"""),
                Ni("max_position_size", """{"maxNotional":1000000}"""),
            ],
        };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.True(HasErrField(errs, "params.maxLevels"));
    }

    [Fact]
    public void CheckWorstExposure_GridExceeds_ShouldReject()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["grid_size", "max_position_size"]),
            phases: new Dictionary<string, RulePhase>
            {
                ["grid_size"] = RulePhase.Size,
                ["max_position_size"] = RulePhase.Risk,
            });
        var chain = new ChainDefinition
        {
            Key = "g",
            Nodes = [
                Ni("grid_size", """{"levels":15,"sizePerLevel":500}"""),
                Ni("max_position_size", """{"maxNotional":3000}"""),
            ],
        };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.True(HasErrField(errs, "params.worstExposure"));
    }

    // ── 校验项 8：加仓封顶 ────────────────────────────────────

    [Fact]
    public void CheckPyramidingCap_MissingCap_ShouldReject()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["pyramiding_size"]),
            phases: new Dictionary<string, RulePhase> { ["pyramiding_size"] = RulePhase.Size });
        var chain = new ChainDefinition { Key = "p", Nodes = [Ni("pyramiding_size", "{}")] };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.True(HasErrField(errs, "nodes"));
    }

    [Fact]
    public void CheckPyramidingCap_WithCap_ShouldPass()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["pyramiding_size", "max_pyramiding"]),
            phases: new Dictionary<string, RulePhase>
            {
                ["pyramiding_size"] = RulePhase.Size,
                ["max_pyramiding"] = RulePhase.Risk,
            });
        var chain = new ChainDefinition
        {
            Key = "p",
            Nodes = [
                Ni("pyramiding_size", "{}"),
                Ni("max_pyramiding", """{"maxLevels":6}"""),
            ],
        };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        // 不应有 "nodes" 字段关于加仓封顶的错误
        Assert.DoesNotContain(errs, e => e.Field == "nodes" && e.Message.Contains("加仓"));
    }

    // ── 校验项 9：数值健全性 ──────────────────────────────────

    [Fact]
    public void CheckNumericSoundness_InvalidValues_ShouldReject()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["x"]),
            phases: new Dictionary<string, RulePhase> { ["x"] = RulePhase.Action });
        var chain = new ChainDefinition
        {
            Key = "n",
            Nodes = [Ni("x", """{"baseAmount":-5,"multiplier":0,"maxDrawdownPct":150}""")],
        };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.True(HasErrField(errs, "params.baseAmount"));
        Assert.True(HasErrField(errs, "params.multiplier"));
        Assert.True(HasErrField(errs, "params.maxDrawdownPct"));
    }

    [Fact]
    public void CheckNumericSoundness_ValidValues_ShouldPass()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["x"]),
            phases: new Dictionary<string, RulePhase> { ["x"] = RulePhase.Action });
        var chain = new ChainDefinition
        {
            Key = "n",
            Nodes = [Ni("x", """{"baseAmount":20,"multiplier":2,"maxDrawdownPct":15}""")],
        };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.False(HasErrField(errs, "params.baseAmount"));
        Assert.False(HasErrField(errs, "params.multiplier"));
        Assert.False(HasErrField(errs, "params.maxDrawdownPct"));
    }

    // ── 校验项 11：Derive emitName 冲突 ────────────────────────

    [Fact]
    public void CheckDeriveEmitNameConflict_WithSignalName_ShouldReject()
    {
        var cfg = DefaultConfig(
            kinds: new HashSet<string>(["crossover_check"]),
            signals: new HashSet<string>(["golden_cross"]),
            phases: new Dictionary<string, RulePhase> { ["crossover_check"] = RulePhase.Derive });
        var chain = new ChainDefinition
        {
            Key = "d",
            Nodes = [Ni("crossover_check", """{"emitName":"golden_cross"}""")],
        };
        var errs = ChainValidator.ValidateChains([chain], cfg);
        Assert.True(HasErrField(errs, "params.emitName"));
    }

    // ── CollectDeriveEmitNames ────────────────────────────────

    [Fact]
    public void CollectDeriveEmitNames_ShouldExtractNames()
    {
        var nodes = new List<NodeInstance>
        {
            Ni("crossover_check", """{"emitName":"golden_cross"}"""),
            Ni("atr_stop_calc", """{"atrMultiplier":2}"""),
            Ni("grid_price_level", """{"emitName":"grid_anchor_deviation"}"""),
        };
        var names = ChainValidator.CollectDeriveEmitNames(nodes);
        Assert.Contains("golden_cross", names);
        Assert.Contains("grid_anchor_deviation", names);
        Assert.Equal(2, names.Count);
    }

    // ── CollectRefParams ──────────────────────────────────────

    [Fact]
    public void CollectRefParams_ShouldTrackSourceKind()
    {
        var nodes = new List<NodeInstance>
        {
            Ni("grid_action", """{"deviationRef":"grid_anchor_deviation"}"""),
            Ni("signal_action", """{"buySignal":"RSI_14"}"""),
        };
        var refParamNames = new Dictionary<string, IReadOnlyList<string>>
        {
            ["grid_action"] = ["deviationRef"],
            ["signal_action"] = ["buySignal"],
        };
        var refs = ChainValidator.CollectRefParams(nodes, refParamNames);
        Assert.Equal("grid_action", refs["grid_anchor_deviation"]);
        Assert.Equal("signal_action", refs["RSI_14"]);
    }

    // ── ValidationError.ToString ──────────────────────────────

    [Fact]
    public void ValidationError_ToString_WithNodeKind()
    {
        var err = new ValidationError("chain1", "field1", "some error", "signal_action");
        Assert.Equal("chain1/signal_action.field1: some error", err.ToString());
    }

    [Fact]
    public void ValidationError_ToString_WithoutNodeKind()
    {
        var err = new ValidationError("chain1", "nodes", "missing action");
        Assert.Equal("chain1.nodes: missing action", err.ToString());
    }
}
