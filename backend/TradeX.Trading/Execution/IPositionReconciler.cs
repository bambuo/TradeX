namespace TradeX.Trading.Execution;

/// <summary>
/// 持仓级对账器：周期性用交易所实际余额校验本地"开仓量"，检测两者漂移并发出告警。
/// 与 <see cref="IOrderReconciler.DetectOrphanOrdersAsync"/> 对称——只检测 + 告警，不自动改仓
/// （账户余额含人工/非策略持仓，自动平改会引入错误）。
/// </summary>
public interface IPositionReconciler
{
    /// <summary>执行一轮持仓对账，返回检测到的漂移条目数。</summary>
    Task<int> ReconcilePositionsAsync(CancellationToken ct = default);
}
