using TradeX.Core.Rules;

namespace TradeX.Trading.Rules;

/// <summary>协调一个策略绑定中的多条规则链。执行所有链并按 §2.4 合并算法合并决策。</summary>
public sealed class ChainCoordinator
{
    private readonly IReadOnlyList<ChainEngine> _chains;

    public ChainCoordinator(IEnumerable<ChainDefinition> definitions, NodeRegistry registry)
    {
        _chains = [.. definitions.Select(d => new ChainEngine(d, registry))];
    }

    /// <summary>执行所有链并合并决策。</summary>
    public async Task<List<StrategyDecision>> EvaluateAsync(
        Dictionary<string, Signal> signals, EvalContext evalCtx, CancellationToken ct = default)
    {
        var results = new List<(List<ActionDecision> Actions, bool Blocked, bool Terminated)>();

        foreach (var engine in _chains)
        {
            var state = new ChainState
            {
                Signals = new(signals),
                Context = evalCtx,
            };
            await engine.ExecuteAsync(state, ct);
            results.Add((state.Actions, state.Blocked, state.Terminated));
        }

        // 收集存活的 Action（R-L2：只跳过 Blocked/Terminated）
        var actions = new List<ActionDecision>();
        var holds = new Dictionary<string, bool>();

        foreach (var (acts, blocked, terminated) in results)
        {
            if (blocked || terminated) continue;
            foreach (var a in acts)
            {
                if (a.Intent == "HOLD")
                {
                    holds[a.Pair] = true;
                    continue;
                }
                actions.Add(a);
            }
        }

        // 按 priority 升序排序（同 priority 时 SELL 优先于 BUY — 平仓安全侧）
        actions.Sort((a, b) =>
        {
            var cmp = a.Priority.CompareTo(b.Priority);
            if (cmp != 0) return cmp;
            // SELL/SELL_ALL < BUY/HOLD: 卖出优先
            return SellFirst(a.Intent).CompareTo(SellFirst(b.Intent));
        });

        static int SellFirst(string intent) => intent switch
        {
            "SELL_ALL" => 0,
            "SELL" => 1,
            _ => 2 // BUY, HOLD etc.
        };

        // 按 pair 分组
        var byPair = new Dictionary<string, List<ActionDecision>>();
        var pairOrder = new List<string>();
        foreach (var a in actions)
        {
            if (!byPair.ContainsKey(a.Pair))
            {
                pairOrder.Add(a.Pair);
                byPair[a.Pair] = [];
            }
            byPair[a.Pair].Add(a);
        }

        // 获取持仓量（用于 SELL clamp）
        var posQty = evalCtx.Position?.Quantity ?? decimal.Zero;
        posQty = Math.Abs(posQty);

        var decisions = new List<StrategyDecision>();
        foreach (var pair in pairOrder)
        {
            decisions.AddRange(ResolvePair(pair, byPair[pair], posQty));
        }

        // Hold 规则：某链 HOLD 且该 pair 无 BUY/SELL → 输出 HOLD
        foreach (var (pair, _) in holds)
        {
            if (!byPair.ContainsKey(pair))
            {
                decisions.Add(new StrategyDecision
                {
                    Pair = pair, Intent = "HOLD",
                });
            }
        }

        return decisions;
    }

    private static List<StrategyDecision> ResolvePair(string pair, List<ActionDecision> group, decimal posQty)
    {
        // 3a. 任一 SELL_ALL → 丢弃所有 BUY/SELL，保留 SELL_ALL
        var sellAll = group.FirstOrDefault(a => a.Intent == "SELL_ALL");
        if (sellAll is not null)
        {
            return
            [
                new StrategyDecision
                {
                    Pair = pair, Intent = "SELL_ALL", OrderType = "MARKET",
                    Quantity = posQty, ActionIds = [sellAll.Id], Reason = "SELL_ALL override",
                }
            ];
        }

        // 3b. 同时有 BUY 和 SELL → 保留 priority 更高（数值更小）的那一侧
        var hasBuy = group.Any(a => a.Intent == "BUY");
        var hasSell = group.Any(a => a.Intent == "SELL");
        if (hasBuy && hasSell)
        {
            var winSide = group[0].Intent;
            group = group.Where(a => a.Intent == winSide).ToList();
        }

        if (group.Count == 0)
            return [];

        if (group[0].Intent == "BUY")
            return AggregateBuy(pair, group);
        return AggregateSell(pair, group, posQty);
    }

    private static List<StrategyDecision> AggregateBuy(string pair, List<ActionDecision> group)
    {
        var buckets = new Dictionary<string, (decimal Qty, decimal Notional, List<string> Ids)>();
        var order = new List<string>();

        foreach (var a in group)
        {
            if (!buckets.ContainsKey(a.OrderType))
            {
                buckets[a.OrderType] = (0, 0, []);
                order.Add(a.OrderType);
            }
            var b = buckets[a.OrderType];
            b.Qty += a.Quantity;
            if (a.Price.HasValue)
                b.Notional += a.Quantity * a.Price.Value;
            b.Ids.Add(a.Id);
            buckets[a.OrderType] = b;
        }

        var out_ = new List<StrategyDecision>();
        foreach (var ot in order)
        {
            var b = buckets[ot];
            var d = new StrategyDecision
            {
                Pair = pair, Intent = "BUY", OrderType = ot,
                Quantity = b.Qty, ActionIds = b.Ids, Reason = "aggregated BUY",
            };
            if (ot == "LIMIT" && b.Qty > 0 && b.Notional > 0)
                d.Price = b.Notional / b.Qty;
            out_.Add(d);
        }
        return out_;
    }

    private static List<StrategyDecision> AggregateSell(string pair, List<ActionDecision> group, decimal posQty)
    {
        var remaining = posQty;
        var buckets = new Dictionary<string, (decimal Qty, List<string> Ids)>();
        var order = new List<string>();

        foreach (var a in group)
        {
            if (remaining <= 0) break;
            var take = Math.Min(a.Quantity, remaining);
            remaining -= take;

            if (!buckets.ContainsKey(a.OrderType))
            {
                buckets[a.OrderType] = (0, []);
                order.Add(a.OrderType);
            }
            var b = buckets[a.OrderType];
            b.Qty += take;
            b.Ids.Add(a.Id);
            buckets[a.OrderType] = b;
        }

        var out_ = new List<StrategyDecision>();
        foreach (var ot in order)
        {
            var b = buckets[ot];
            var intent = order.Count == 1 && b.Qty == posQty && posQty > 0 ? "SELL_ALL" : "SELL";
            out_.Add(new StrategyDecision
            {
                Pair = pair, Intent = intent, OrderType = ot,
                Quantity = b.Qty, ActionIds = b.Ids, Reason = "aggregated SELL",
            });
        }
        return out_;
    }
}
