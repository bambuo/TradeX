namespace TradeX.Trading.Execution;

public interface IOrderReconciler
{
    Task ReconcileAsync(CancellationToken ct = default);
}
