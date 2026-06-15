namespace TradeX.Trading.Rules;

/// <summary>
/// 多链决策合并算法纯函数规格（设计文档 §2.4「决策合并算法」）。
///
/// 覆盖逻辑：
///   R-L1：priority 数值【越小=优先级越高】，冲突保留更小者
///   R-L2：只跳过 Blocked/Terminated 的链，不因"非致命 Error"整链丢弃
///   R5  ：混合订单类型不合并；多 LIMIT 走 VWAP；SELL 聚合对持仓 clamp，满仓转 SELL_ALL
/// </summary>
public static class DecisionMerge
{
    public const string IntentBuy = "BUY";
    public const string IntentSell = "SELL";
    public const string IntentSellAll = "SELL_ALL";
    public const string IntentHold = "HOLD";
    public const string OrderMarket = "MARKET";
    public const string OrderLimit = "LIMIT";

    /// <summary>单条链的动作决策。</summary>
    public sealed record ActionDecision(
        string Id,
        string Pair,
        string Intent,
        decimal Quantity,
        string OrderType,
        decimal? Price,
        int Priority);

    /// <summary>单条链的执行结果。</summary>
    public sealed record ChainResult(List<ActionDecision> Actions, bool Blocked, bool Terminated);

    /// <summary>合并后的最终策略决策。</summary>
    public sealed record StrategyDecision(
        string Pair,
        string Intent,
        decimal Quantity,
        string OrderType,
        decimal? Price,
        List<string> ActionIds);

    /// <summary>
    /// 合并多条链的决策。positionQty 提供各 pair 当前持仓量，用于 SELL clamp。
    /// </summary>
    public static List<StrategyDecision> Merge(List<ChainResult> chains, Dictionary<string, decimal> positionQty)
    {
        // 1. 收集：只跳过 Blocked/Terminated（R-L2）。HOLD 单独记。
        var actions = new List<ActionDecision>();
        var holds = new Dictionary<string, bool>();

        foreach (var c in chains)
        {
            if (c.Blocked || c.Terminated)
                continue;

            foreach (var a in c.Actions)
            {
                if (a.Intent == IntentHold)
                {
                    holds[a.Pair] = true;
                    continue;
                }
                actions.Add(a);
            }
        }

        // 2. 按 priority 升序（小=高，R-L1）。稳定排序保证同优先级保持链内顺序。
        actions.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        // 3. 按 pair 分组（保持首次出现顺序，输出稳定）
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

        var output = new List<StrategyDecision>();
        foreach (var pair in pairOrder)
        {
            output.AddRange(ResolvePair(pair, byPair[pair], positionQty.GetValueOrDefault(pair)));
        }

        // 4. Hold 规则：某链 HOLD 且该 pair 无 BUY/SELL → 输出 HOLD
        foreach (var pair in holds.Keys.OrderBy(k => k))
        {
            if (!byPair.ContainsKey(pair))
                output.Add(new StrategyDecision(pair, IntentHold, 0, OrderMarket, null, []));
        }

        return output;
    }

    // ─── 单 pair 内决策解析 ───

    private static List<StrategyDecision> ResolvePair(string pair, List<ActionDecision> group, decimal posQty)
    {
        // 3a. 任一 SELL_ALL → 丢弃所有 BUY/SELL，保留 SELL_ALL（按持仓全平）
        foreach (var a in group)
        {
            if (a.Intent == IntentSellAll)
            {
                return [new StrategyDecision(pair, IntentSellAll, posQty, OrderMarket, null, [a.Id])];
            }
        }

        // 3b. 同时有 BUY 和 SELL → 保留 priority 更高（数值更小）的那一侧
        var hasBuy = group.Any(a => a.Intent == IntentBuy);
        var hasSell = group.Any(a => a.Intent == IntentSell);

        if (hasBuy && hasSell)
        {
            var winSide = group[0].Intent; // group 已按 priority 升序，首个即最高优先级
            group = group.Where(a => a.Intent == winSide).ToList();
        }

        return group.Count > 0 && group[0].Intent == IntentBuy
            ? AggregateBuy(pair, group)
            : AggregateSell(pair, group, posQty);
    }

    // 3c. BUY 聚合：按 orderType 分桶
    private static List<StrategyDecision> AggregateBuy(string pair, List<ActionDecision> group)
    {
        var buckets = new Dictionary<string, (decimal Qty, decimal Notional, List<string> Ids)>();
        var order = new List<string>();

        foreach (var a in group)
        {
            if (!buckets.TryGetValue(a.OrderType, out var b))
            {
                b = (0, 0, []);
                buckets[a.OrderType] = b;
                order.Add(a.OrderType);
            }
            b.Qty += a.Quantity;
            b.Ids.Add(a.Id);
            if (a.Price.HasValue)
                b.Notional += a.Quantity * a.Price.Value;
            buckets[a.OrderType] = b;
        }

        var output = new List<StrategyDecision>();
        foreach (var ot in order)
        {
            var (qty, notional, ids) = buckets[ot];
            var d = new StrategyDecision(pair, IntentBuy, qty, ot, null, ids);
            if (ot == OrderLimit && qty > 0 && notional > 0)
                d = d with { Price = notional / qty };
            output.Add(d);
        }
        return output;
    }

    // 3d. SELL 聚合：按 priority 顺序累积，总量 clamp 到持仓
    private static List<StrategyDecision> AggregateSell(string pair, List<ActionDecision> group, decimal posQty)
    {
        var remaining = posQty;
        var buckets = new Dictionary<string, (decimal Qty, List<string> Ids)>();
        var order = new List<string>();

        foreach (var a in group)
        {
            if (remaining <= 0) break; // 已卖到持仓上限，丢弃多余 SELL（防卖超）

            var take = a.Quantity;
            if (take > remaining) take = remaining;
            remaining -= take;

            if (!buckets.TryGetValue(a.OrderType, out var b))
            {
                b = (0, []);
                buckets[a.OrderType] = b;
                order.Add(a.OrderType);
            }
            b.Qty += take;
            b.Ids.Add(a.Id);
            buckets[a.OrderType] = b;
        }

        var output = new List<StrategyDecision>();
        foreach (var ot in order)
        {
            var (qty, ids) = buckets[ot];
            var intent = order.Count == 1 && qty == posQty && posQty > 0 ? IntentSellAll : IntentSell;
            output.Add(new StrategyDecision(pair, intent, qty, ot, null, ids));
        }
        return output;
    }
}
