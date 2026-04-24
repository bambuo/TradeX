using System.Collections.Concurrent;
using TradeX.Core.Interfaces;

namespace TradeX.Exchange;

public class TokenBucketRateLimiter : IExchangeRateLimiter, IDisposable
{
    private readonly ConcurrentDictionary<string, TokenBucket> _buckets = new();
    private readonly Timer _timer;

    public TokenBucketRateLimiter()
    {
        _timer = new Timer(_ => RefillAll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    public ValueTask<IDisposable> AcquireAsync(string exchange, string accountId, string endpointGroup, int permits = 1, CancellationToken ct = default)
    {
        var key = $"{exchange}:{accountId}:{endpointGroup}";
        var bucket = _buckets.GetOrAdd(key, _ => CreateDefaultBucket(exchange, endpointGroup));

        if (bucket.TryAcquire(permits))
            return ValueTask.FromResult<IDisposable>(new NoopDisposable());

        bucket.DelayQueue.Enqueue(permits);
        return ValueTask.FromResult<IDisposable>(new NoopDisposable());
    }

    private static TokenBucket CreateDefaultBucket(string exchange, string endpointGroup)
    {
        var (capacity, refillPerSecond) = (exchange, endpointGroup) switch
        {
            ("Binance", "rest") => (1200, 20),
            ("Binance", "ws") => (5, 1),
            ("OKX", "rest") => (600, 10),
            ("OKX", "ws") => (5, 1),
            ("Gate", "rest") => (600, 10),
            ("Bybit", "rest") => (600, 10),
            ("HTX", "rest") => (600, 10),
            _ => (100, 5)
        };
        return new TokenBucket(capacity, refillPerSecond);
    }

    private void RefillAll()
    {
        foreach (var bucket in _buckets.Values)
            bucket.Refill();
    }

    public void Dispose() => _timer.Dispose();

    private sealed class TokenBucket(int capacity, int refillPerSecond)
    {
        private long _tokens = capacity;
        private readonly int _maxTokens = capacity;
        private readonly int _refillRate = refillPerSecond;

        public Queue<int> DelayQueue { get; } = new();

        public bool TryAcquire(int permits)
        {
            var current = Interlocked.Add(ref _tokens, -permits);
            if (current >= 0) return true;
            Interlocked.Add(ref _tokens, permits);
            return false;
        }

        public void Refill()
        {
            var current = Volatile.Read(ref _tokens);
            var newTokens = Math.Min(current + _refillRate, _maxTokens);
            Interlocked.Exchange(ref _tokens, newTokens);

            while (DelayQueue.TryDequeue(out var permits))
            {
                if (TryAcquire(permits)) break;
                DelayQueue.Enqueue(permits);
                break;
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
