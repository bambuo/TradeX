namespace TradeX.Trading;

public interface IOrderReconciler
{
    Task ReconcileAsync(CancellationToken ct = default);
}
