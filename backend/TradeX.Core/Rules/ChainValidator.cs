using System.Text.Json;
using TradeX.Core.Enums;

namespace TradeX.Core.Rules;

/// <summary>链配置静态校验错误（ChainValidator.ValidateChains 返回，全量不短路）。</summary>
public sealed record ValidationError(
    string ChainKey,
    string Field,
    string Message,
    string NodeKind = ""
)
{
    /// <summary>格式化错误信息。</summary>
    public override string ToString() =>
        NodeKind is { Length: > 0 }
            ? $"{ChainKey}/{NodeKind}.{Field}: {Message}"
            : $"{ChainKey}.{Field}: {Message}";
}

/// <summary>ChainValidator 所需的依赖注入配置。</summary>
/// <param name="RegisteredKinds">已注册的节点 Kind 集合。</param>
/// <param name="RegisteredSignalNames">已注册的信号名称集合。</param>
/// <param name="RegisteredEmitNames">节点硬编码的衍生输出名集合（NodeDescriptor.EmitNames）。</param>
/// <param name="NodePhases">每个已注册 Kind 对应的 Phase。</param>
/// <param name="AllowDuplicateKinds">允许同 Phase 内重复的 Kind 集合。</param>
/// <param name="RefParamNames">每个 Kind 中 Type="ref" 的参数名列表。</param>
/// <param name="ActionProducerKinds">直接产出交易决策的节点 Kind 集合（即使不在 Action/Override 阶段）。</param>
public sealed record ChainValidatorConfig(
    IReadOnlySet<string> RegisteredKinds,
    IReadOnlySet<string> RegisteredSignalNames,
    IReadOnlySet<string> RegisteredEmitNames,
    IReadOnlyDictionary<string, RulePhase> NodePhases,
    IReadOnlySet<string> AllowDuplicateKinds,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RefParamNames,
    IReadOnlySet<string> ActionProducerKinds
);

/// <summary>
/// 规则链配置静态校验器。
/// 对一组链定义执行全量校验（校验项 1-10），返回所有错误（不短路）。
/// </summary>
/// <remarks>
/// 校验项清单：
/// <list type="number">
///   <item>节点 Kind 存在性</item>
///   <item>节点参数可解析</item>
///   <item>信号引用存在性</item>
///   <item>缺少必需阶段（至少一个 Action 或 Override 节点）</item>
///   <item>依赖无环（DFS 三色法）</item>
///   <item>Phase 内重复</item>
///   <item>最坏敞口有界</item>
///   <item>加仓策略必须有层数封顶</item>
///   <item>数值健全性</item>
///   <item>跨品种相关性参数合法</item>
///   <item>Derive emitName 不得与已注册信号同名</item>
/// </list>
/// </remarks>
public static class ChainValidator
{
    /// <summary>对一组链定义执行全量校验，返回所有错误（不短路）。</summary>
    public static List<ValidationError> ValidateChains(
        IReadOnlyList<ChainDefinition> chains, ChainValidatorConfig config)
    {
        var errors = new List<ValidationError>();
        foreach (var chain in chains)
            errors.AddRange(ValidateChain(chain, config));
        return errors;
    }

    private static List<ValidationError> ValidateChain(
        ChainDefinition chain, ChainValidatorConfig config)
    {
        var errors = new List<ValidationError>();
        errors.AddRange(CheckKindExistence(chain, config));
        errors.AddRange(CheckParamsParsable(chain));
        errors.AddRange(CheckSignalRefExistence(chain, config));
        errors.AddRange(CheckRequiredPhase(chain, config));
        errors.AddRange(CheckDependencyAcyclic(chain));
        errors.AddRange(CheckPhaseDuplicate(chain, config));
        errors.AddRange(CheckWorstExposure(chain));
        errors.AddRange(CheckPyramidingCap(chain));
        errors.AddRange(CheckNumericSoundness(chain));
        errors.AddRange(CheckCorrelationParams(chain));
        errors.AddRange(CheckDeriveEmitNameConflict(chain, config));
        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 1：节点 Kind 存在性
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckKindExistence(
        ChainDefinition chain, ChainValidatorConfig config)
    {
        var errors = new List<ValidationError>();
        foreach (var node in chain.Nodes)
        {
            if (!config.RegisteredKinds.Contains(node.NodeKind))
            {
                errors.Add(new ValidationError(
                    chain.Key, "nodeKind",
                    $"节点类型 \"{node.NodeKind}\" 未注册",
                    node.NodeKind));
            }
        }
        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 2：节点参数可解析
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckParamsParsable(ChainDefinition chain)
    {
        var errors = new List<ValidationError>();
        foreach (var node in chain.Nodes)
        {
            if (node.Params.ValueKind == JsonValueKind.Undefined
                || node.Params.ValueKind == JsonValueKind.Null)
                continue;

            // JsonElement 已被 System.Text.Json 解析，此处校验是否为合法 object。
            if (node.Params.ValueKind != JsonValueKind.Object)
            {
                errors.Add(new ValidationError(
                    chain.Key, "params",
                    "参数不是合法的 JSON object",
                    node.NodeKind));
            }
        }
        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 3：信号引用存在性
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckSignalRefExistence(
        ChainDefinition chain, ChainValidatorConfig config)
    {
        var errors = new List<ValidationError>();
        var refs = CollectRefParams(chain.Nodes, config.RefParamNames);
        var emitNames = CollectDeriveEmitNames(chain.Nodes);

        foreach (var (refName, nodeKind) in refs)
        {
            if (config.RegisteredSignalNames.Contains(refName))
                continue;
            if (config.RegisteredEmitNames.Contains(refName))
                continue;
            if (emitNames.Contains(refName))
                continue;

            errors.Add(new ValidationError(
                chain.Key, "params.ref",
                $"引用的信号 \"{refName}\" 既不是已注册信号也不是衍生输出",
                nodeKind));
        }
        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 4：阶段完整性（至少一个 Action 或 Override 节点）
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckRequiredPhase(
        ChainDefinition chain, ChainValidatorConfig config)
    {
        foreach (var node in chain.Nodes)
        {
            if (config.NodePhases.TryGetValue(node.NodeKind, out var phase)
                && phase is RulePhase.Action or RulePhase.Override)
                return [];

            if (config.ActionProducerKinds.Contains(node.NodeKind))
                return [];
        }

        return [new ValidationError(
            chain.Key, "nodes",
            "规则链缺少 Action 或 Override 阶段节点，无法产生交易决策")];
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 5：依赖无环（DFS 三色法）
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckDependencyAcyclic(ChainDefinition chain)
    {
        var errors = new List<ValidationError>();

        // 构建节点信息：每个节点的依赖（从 params 中的 string 值提取）。
        var nodeInfos = new List<(string Kind, List<string> Deps)>();
        var emitToIndex = new Dictionary<string, int>();

        for (var i = 0; i < chain.Nodes.Count; i++)
        {
            var node = chain.Nodes[i];
            var deps = new List<string>();

            if (node.Params.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in node.Params.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                        deps.Add(prop.Value.GetString()!);
                }

                // 记录 emitName → 节点索引。
                if (node.Params.TryGetProperty("emitName", out var emitProp)
                    && emitProp.ValueKind == JsonValueKind.String
                    && emitProp.GetString() is { Length: > 0 } emitName)
                {
                    emitToIndex[emitName] = i;
                }
            }

            nodeInfos.Add((node.NodeKind, deps));
        }

        // 构建邻接表：如果节点 A 的依赖字符串匹配另一个节点 B 的 emitName，则 A → B。
        var adjList = new Dictionary<int, List<int>>();
        for (var i = 0; i < nodeInfos.Count; i++)
        {
            foreach (var dep in nodeInfos[i].Deps)
            {
                if (emitToIndex.TryGetValue(dep, out var j) && j != i)
                {
                    if (!adjList.ContainsKey(i))
                        adjList[i] = [];
                    adjList[i].Add(j);
                }
            }
        }

        // DFS 三色法：0=white, 1=gray, 2=black。
        var colors = new int[nodeInfos.Count];

        void Dfs(int u)
        {
            colors[u] = 1; // gray
            if (adjList.TryGetValue(u, out var neighbors))
            {
                foreach (var v in neighbors)
                {
                    if (colors[v] == 1)
                    {
                        errors.Add(new ValidationError(
                            chain.Key, "deps",
                            $"检测到 {nodeInfos[u].Kind} 与 {nodeInfos[v].Kind} 之间存在循环依赖",
                            nodeInfos[u].Kind));
                    }
                    else if (colors[v] == 0)
                    {
                        Dfs(v);
                    }
                }
            }
            colors[u] = 2; // black
        }

        for (var i = 0; i < nodeInfos.Count; i++)
        {
            if (colors[i] == 0)
                Dfs(i);
        }

        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 6：Phase 内重复
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckPhaseDuplicate(
        ChainDefinition chain, ChainValidatorConfig config)
    {
        var errors = new List<ValidationError>();
        // phase → kind → count
        var phaseKindCount = new Dictionary<RulePhase, Dictionary<string, int>>();

        foreach (var node in chain.Nodes)
        {
            if (!config.NodePhases.TryGetValue(node.NodeKind, out var phase))
                continue;

            if (!phaseKindCount.TryGetValue(phase, out var kindCount))
            {
                kindCount = [];
                phaseKindCount[phase] = kindCount;
            }

            kindCount.TryGetValue(node.NodeKind, out var count);
            kindCount[node.NodeKind] = count + 1;
        }

        foreach (var (phase, kinds) in phaseKindCount)
        {
            foreach (var (kind, count) in kinds)
            {
                if (count > 1 && !config.AllowDuplicateKinds.Contains(kind))
                {
                    errors.Add(new ValidationError(
                        chain.Key, "phase",
                        $"节点类型 \"{kind}\" 在 {phase} 阶段中重复（数量: {count}）",
                        kind));
                }
            }
        }

        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 7：最坏敞口有界
    // ────────────────────────────────────────────────────────────

    private static readonly HashSet<string> MartingaleActionKinds =
        ["martingale_action"];

    private static readonly HashSet<string> CapKinds =
        ["max_pyramiding"];

    private static readonly HashSet<string> PyramidingKinds =
        ["pyramiding_size", "grid_size"];

    private static List<ValidationError> CheckWorstExposure(ChainDefinition chain)
    {
        var errors = new List<ValidationError>();

        // 查找 max_position_size 的 maxNotional。
        decimal? maxNotional = null;
        foreach (var node in chain.Nodes)
        {
            if (node.NodeKind == "max_position_size")
            {
                if (TryGetDecimalParam(node, "maxNotional", out var mn))
                    maxNotional = mn;
            }
        }

        // 检查马丁格尔最坏敞口：Σ baseAmount × multiplier^k (k=0..maxLevels-1)。
        foreach (var node in chain.Nodes)
        {
            if (!MartingaleActionKinds.Contains(node.NodeKind))
                continue;

            if (!TryGetDecimalParam(node, "baseAmount", out var baseAmount)
                || !TryGetDecimalParam(node, "multiplier", out var multiplier)
                || !TryGetDecimalParam(node, "maxLevels", out var maxLevelsRaw))
                continue;

            var maxLevels = (int)maxLevelsRaw;
            if (maxLevels <= 0) continue;

            var worstExposure = CalcWorstMartingale(baseAmount, multiplier, maxLevels);

            if (maxNotional.HasValue && worstExposure > maxNotional.Value)
            {
                errors.Add(new ValidationError(
                    chain.Key, "params.worstExposure",
                    $"最坏敞口 {worstExposure}（baseAmount={baseAmount} × multiplier^{maxLevels}）超过上限 maxNotional={maxNotional.Value}",
                    node.NodeKind));
            }

            // 溢出守卫。
            if (multiplier > 1m && maxLevels > 100)
            {
                errors.Add(new ValidationError(
                    chain.Key, "params.maxLevels",
                    $"maxLevels={maxLevels} 配合 multiplier={multiplier} 可能导致 decimal 溢出",
                    node.NodeKind));
            }
        }

        // 检查网格最坏敞口：Σ 各层 size。
        foreach (var node in chain.Nodes)
        {
            if (node.NodeKind != "grid_size") continue;

            if (!TryGetDecimalParam(node, "levels", out var gridLevels)
                || !TryGetDecimalParam(node, "sizePerLevel", out var sizePerLevel))
                continue;

            var levels = (int)gridLevels;
            var worstExposure = sizePerLevel * levels;

            if (maxNotional.HasValue && worstExposure > maxNotional.Value)
            {
                errors.Add(new ValidationError(
                    chain.Key, "params.worstExposure",
                    $"网格最坏敞口 {worstExposure}（sizePerLevel={sizePerLevel} × levels={levels}）超过上限 maxNotional={maxNotional.Value}",
                    node.NodeKind));
            }
        }

        return errors;
    }

    /// <summary>计算马丁格尔最坏累计敞口：Σ baseAmount × multiplier^k (k=0..maxLevels-1)。</summary>
    private static decimal CalcWorstMartingale(decimal baseAmount, decimal multiplier, int maxLevels)
    {
        var total = 0m;
        var power = 1m;

        // 安全上限：防止指数爆炸。
        const int safeMax = 20;
        if (maxLevels > safeMax)
            maxLevels = safeMax;

        for (var k = 0; k < maxLevels; k++)
        {
            var term = baseAmount * power;
            total += term;

            // 溢出保护。
            if (term > decimal.MaxValue / 2m)
                return decimal.MaxValue;

            power *= multiplier;
        }

        return total;
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 8：加仓策略必须有层数封顶
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckPyramidingCap(ChainDefinition chain)
    {
        var hasPyramiding = false;
        var hasCap = false;

        foreach (var node in chain.Nodes)
        {
            if (PyramidingKinds.Contains(node.NodeKind))
                hasPyramiding = true;
            if (CapKinds.Contains(node.NodeKind))
                hasCap = true;
        }

        if (hasPyramiding && !hasCap)
        {
            return [new ValidationError(
                chain.Key, "nodes",
                "规则链包含加仓/网格节点但缺少 max_pyramiding 封顶，不允许无限制的仓位累积")];
        }

        return [];
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 9：数值健全性
    // ────────────────────────────────────────────────────────────

    /// <summary>必须为正的参数名。</summary>
    private static readonly Dictionary<string, HashSet<string>?> MustBePositive = new()
    {
        ["baseAmount"] = null,
        ["multiplier"] = null,
        ["maxNotional"] = null,
        ["atrMultiplier"] = null,
        ["minNotional"] = null,
    };

    /// <summary>百分比类参数 (0, 100]。</summary>
    private static readonly HashSet<string> PercentParams =
        ["maxDrawdownPct", "dailyLossLimitPct", "riskPercent", "allocationPct"];

    private static List<ValidationError> CheckNumericSoundness(ChainDefinition chain)
    {
        var errors = new List<ValidationError>();

        foreach (var node in chain.Nodes)
        {
            if (node.Params.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var paramName in MustBePositive.Keys)
            {
                if (TryGetDecimalParam(node, paramName, out var v) && v <= 0)
                {
                    errors.Add(new ValidationError(
                        chain.Key, $"params.{paramName}",
                        $"{paramName} 必须大于 0，当前值 {v}",
                        node.NodeKind));
                }
            }

            foreach (var paramName in PercentParams)
            {
                if (TryGetDecimalParam(node, paramName, out var v)
                    && (v <= 0 || v > 100))
                {
                    errors.Add(new ValidationError(
                        chain.Key, $"params.{paramName}",
                        $"{paramName} 必须在 (0, 100] 范围内，当前值 {v}",
                        node.NodeKind));
                }
            }
        }

        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 校验项 10：跨品种相关性参数合法
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckCorrelationParams(ChainDefinition chain)
    {
        var errors = new List<ValidationError>();

        foreach (var node in chain.Nodes)
        {
            if (node.NodeKind != "max_correlation") continue;

            if (node.Params.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                errors.Add(new ValidationError(
                    chain.Key, "params",
                    "max_correlation 节点缺少参数",
                    node.NodeKind));
                continue;
            }

            if (TryGetDecimalParam(node, "maxCorrelation", out var corr)
                && (corr < -1m || corr > 1m))
            {
                errors.Add(new ValidationError(
                    chain.Key, "params.maxCorrelation",
                    $"maxCorrelation 必须是 [-1, 1] 之间的值，当前值 {corr}",
                    node.NodeKind));
            }
        }

        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 附加校验：Derive emitName 不得与已注册信号同名，链内不重复
    // ────────────────────────────────────────────────────────────

    private static List<ValidationError> CheckDeriveEmitNameConflict(
        ChainDefinition chain, ChainValidatorConfig config)
    {
        var errors = new List<ValidationError>();
        var emitNames = CollectDeriveEmitNames(chain.Nodes);
        var seen = new Dictionary<string, string>();

        foreach (var emitName in emitNames)
        {
            if (config.RegisteredSignalNames.Contains(emitName))
            {
                errors.Add(new ValidationError(
                    chain.Key, "params.emitName",
                    $"衍生输出的名称 \"{emitName}\" 与已注册信号冲突"));
            }

            if (seen.TryGetValue(emitName, out var firstKind))
            {
                errors.Add(new ValidationError(
                    chain.Key, "params.emitName",
                    $"同链内衍生输出名称 \"{emitName}\" 重复（出现于 {firstKind} 和当前节点）"));
            }
            else
            {
                seen[emitName] = emitName;
            }
        }

        return errors;
    }

    // ────────────────────────────────────────────────────────────
    // 公共辅助方法（供 ChainValidator 和外部 ResolveHelpers 使用）
    // ────────────────────────────────────────────────────────────

    /// <summary>收集 Derive 节点的 emitName 参数值。</summary>
    public static List<string> CollectDeriveEmitNames(IReadOnlyList<NodeInstance> nodes)
    {
        var names = new List<string>();
        foreach (var node in nodes)
        {
            if (node.Params.ValueKind != JsonValueKind.Object) continue;
            if (!node.Params.TryGetProperty("emitName", out var emitProp)) continue;
            if (emitProp.ValueKind != JsonValueKind.String) continue;
            if (emitProp.GetString() is { Length: > 0 } name)
                names.Add(name);
        }
        return names;
    }

    /// <summary>
    /// 收集 ref 类型参数引用，返回 (引用名称 → 引用来源节点 Kind) 的映射。
    /// </summary>
    public static Dictionary<string, string> CollectRefParams(
        IReadOnlyList<NodeInstance> nodes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> refParamNames)
    {
        var refs = new Dictionary<string, string>();
        foreach (var node in nodes)
        {
            if (node.Params.ValueKind != JsonValueKind.Object) continue;
            if (!refParamNames.TryGetValue(node.NodeKind, out var paramNames)) continue;

            foreach (var paramName in paramNames)
            {
                if (!node.Params.TryGetProperty(paramName, out var prop)) continue;
                if (prop.ValueKind != JsonValueKind.String) continue;
                if (prop.GetString() is { Length: > 0 } refName)
                    refs[refName] = node.NodeKind;
            }
        }
        return refs;
    }

    /// <summary>尝试从节点的 JSON 参数中提取 decimal 值。</summary>
    private static bool TryGetDecimalParam(NodeInstance node, string key, out decimal value)
    {
        value = 0;
        if (node.Params.ValueKind != JsonValueKind.Object) return false;
        if (!node.Params.TryGetProperty(key, out var prop)) return false;

        return prop.ValueKind switch
        {
            JsonValueKind.Number => prop.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(prop.GetString(), out value),
            _ => false,
        };
    }
}
