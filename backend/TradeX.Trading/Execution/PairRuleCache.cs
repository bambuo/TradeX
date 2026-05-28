using System.Collections.Concurrent;
using TradeX.Core.Interfaces;

namespace TradeX.Trading.Execution;

/// <summary>
/// 按交易所缓存交易对规则（GetPairRulesAsync 返回全量、TTL 1h，符号规则极少变动）。
/// 供 <see cref="TradeExecutor"/> 在下单前按单个交易对取 stepSize/minNotional 等，
/// 避免每单全量拉取。查不到/拉取失败返回 null，调用方应优雅降级（不阻断交易）。
/// </summary>
public sealed class PairRuleCache
{
    private sealed record Entry(Dictionary<string, PairRule> ByPair, DateTime LoadedAt);

    private readonly ConcurrentDictionary<Guid, Entry> _cache = new();
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    public async Task<PairRule?> GetRuleAsync(Guid exchangeId, IExchangeClient client, string pair, CancellationToken ct)
    {
        if (!_cache.TryGetValue(exchangeId, out var entry) || DateTime.UtcNow - entry.LoadedAt > Ttl)
        {
            var rules = await client.GetPairRulesAsync(ct);
            if (rules.Length == 0) return null; // 拉取失败/空，降级
            var byPair = new Dictionary<string, PairRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rules)
                byPair[Normalize(r.Pair)] = r;
            entry = new Entry(byPair, DateTime.UtcNow);
            _cache[exchangeId] = entry;
        }
        return entry.ByPair.TryGetValue(Normalize(pair), out var rule) ? rule : null;
    }

    // 归一化各家原生符号（BTC-USDT / BTC_USDT / btcusdt）与统一 BTCUSDT 形式比对
    private static string Normalize(string pair)
        => pair.Replace("-", "").Replace("_", "").Replace("/", "").ToUpperInvariant();
}
