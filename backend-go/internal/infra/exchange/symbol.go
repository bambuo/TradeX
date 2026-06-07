package exchange

import "strings"

// quoteAssets 顺序敏感：更长/更具体的后缀在前，避免 USDT 被 USD 提前匹配。
// 对应 C# SymbolFormatter.QuoteAssets。
var quoteAssets = []string{"USDT", "USDC", "FDUSD", "TUSD", "DAI", "USD", "EUR", "BTC", "ETH", "BNB"}

// SymbolWithSeparator 把无分隔符的合并符号（BTCUSDT）按已知计价币后缀拆出并插入分隔符
// （OKX 用 '-'、Gate 用 '_'）。比朴素的 Replace("USDT", ...) 安全；未匹配到已知后缀时原样返回。
// 对应 C# SymbolFormatter.WithSeparator。
func SymbolWithSeparator(pair string, separator byte) string {
	if pair == "" || strings.IndexByte(pair, separator) >= 0 {
		return pair
	}
	for _, quote := range quoteAssets {
		if len(pair) > len(quote) && strings.EqualFold(pair[len(pair)-len(quote):], quote) {
			base := pair[:len(pair)-len(quote)]
			return base + string(separator) + pair[len(pair)-len(quote):]
		}
	}
	return pair
}
