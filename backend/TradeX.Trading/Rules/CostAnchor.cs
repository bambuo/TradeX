namespace TradeX.Trading.Rules;

/// <summary>
/// 成本中枢再平衡纯函数规格（设计文档 §3.5 cost_anchored_rebalance）。
///
/// 关键性质（防级联）：动作只把仓位推向 target，已在 target 即 HOLD。
/// 因此"一次 +2% 偏离"只会减一档，而非把整仓循环卖光。
///
/// rung = floor(deviation / stepPercent)
/// 价低于均价 → deviation 为负 → rung 为负 → 加仓侧；价高于均价 → rung 为正 → 减仓侧。
/// </summary>
public static class CostAnchor
{
    /// <summary>中枢再平衡参数。</summary>
    public sealed record Params
    {
        /// <summary>每档偏离百分比（如 2.0），须 > 2×(手续费+滑点)。</summary>
        public decimal StepPercent { get; init; }

        /// <summary>每档加/减的量。</summary>
        public decimal StepQty { get; init; }

        /// <summary>中枢目标仓位（deviation=0 时持有量）。</summary>
        public decimal BaseQty { get; init; }

        /// <summary>向下最多加几档。</summary>
        public int MaxRungsDown { get; init; }

        /// <summary>向上最多减几档（减到 0 即离场）。</summary>
        public int MaxRungsUp { get; init; }
    }

    /// <summary>再平衡动作结果。</summary>
    public sealed record Action(string Side, decimal Quantity);

    public const string SideBuy = "BUY";
    public const string SideReduce = "REDUCE";
    public const string SideHold = "HOLD";

    /// <summary>
    /// 计算把仓位「补/减到目标」的设定点动作。
    ///
    /// deviationPct：DEVIATION_FROM_AVG（相对持仓均价的偏离%，价低于均价为负）
    /// currentQty  ：当前持仓量
    /// </summary>
    public static Action Rebalance(decimal deviationPct, decimal currentQty, Params p)
    {
        var rung = ClampRung(RungOf(deviationPct, p.StepPercent), p.MaxRungsDown, p.MaxRungsUp);

        // target = base − rung×stepQty：rung 负（价低）→ target 变大（加仓）；rung 正（价高）→ target 变小（减仓）
        var target = p.BaseQty - p.StepQty * rung;

        // clamp 到 [0, base + maxDown×step]
        var maxQty = p.BaseQty + p.StepQty * p.MaxRungsDown;
        if (target > maxQty) target = maxQty;
        if (target < 0) target = 0;

        var diff = target - currentQty;
        return diff switch
        {
            > 0 when rung <= 0 => new Action(SideBuy, diff),           // 仅在价≤成本时加仓
            < 0 when rung >= 1 => new Action(SideReduce, -diff),       // 仅在价>成本时减仓
            _ => new Action(SideHold, 0),
        };
    }

    // ─── helpers ───

    private static long RungOf(decimal deviation, decimal step)
    {
        if (step == 0) return 0;
        return (long)Math.Floor(deviation / step);
    }

    private static long ClampRung(long r, int maxDown, int maxUp)
    {
        if (r > maxUp) return maxUp;
        if (r < -maxDown) return -maxDown;
        return r;
    }
}
