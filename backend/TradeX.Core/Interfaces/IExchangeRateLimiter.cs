namespace TradeX.Core.Interfaces;

public interface IExchangeRateLimiter
{
    ValueTask<IDisposable> AcquireAsync(string exchange, string accountId, string endpointGroup, int permits = 1, CancellationToken ct = default);
}
