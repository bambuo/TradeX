namespace TradeX.Trading.Execution;

public interface IOrderReconciler
{
    /// <summary>本地 → 交易所方向: 把本地 Pending 订单与交易所最新状态对齐.</summary>
    Task ReconcileAsync(CancellationToken ct = default);

    /// <summary>
    /// 交易所 → 本地方向: 拉取交易所未结订单, 检测本地缺失的"孤儿"订单.
    /// 发现孤儿时写入 OutboxEvent("OrphanOrderDetected") 异步推送告警, 不直接修改任何业务表.
    /// 通常仅在启动时调用; 周期对账由 <see cref="ReconcileAsync"/> 承担.
    /// </summary>
    /// <returns>检测到的孤儿订单数量.</returns>
    Task<int> DetectOrphanOrdersAsync(CancellationToken ct = default);
}
