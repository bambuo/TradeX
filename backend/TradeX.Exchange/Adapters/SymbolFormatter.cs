namespace TradeX.Exchange.Adapters;

/// <summary>
/// 交易对符号格式化：把无分隔符的合并符号（BTCUSDT）按已知计价币后缀拆出并插入分隔符
/// （OKX 用 '-'、Gate 用 '_'）。比朴素的 .Replace("USDT", ...) 安全，避免在 USDT 作基础币
/// （USDTTRY）或 USDC/USD 计价时误转。未匹配到已知后缀时原样返回。
/// </summary>
internal static class SymbolFormatter
{
    // 顺序敏感：更长 / 更具体的后缀在前，避免 USDT 被 USD 提前匹配
    private static readonly string[] QuoteAssets =
        ["USDT", "USDC", "FDUSD", "TUSD", "DAI", "USD", "EUR", "BTC", "ETH", "BNB"];

    public static string WithSeparator(string pair, char separator)
    {
        if (string.IsNullOrEmpty(pair) || pair.Contains(separator))
            return pair;

        foreach (var quote in QuoteAssets)
        {
            if (pair.Length > quote.Length && pair.EndsWith(quote, StringComparison.OrdinalIgnoreCase))
                return string.Concat(pair.AsSpan(0, pair.Length - quote.Length), separator.ToString(), pair.AsSpan(pair.Length - quote.Length));
        }

        return pair;
    }
}
