package exchange

import "testing"

func TestSymbolWithSeparator(t *testing.T) {
	cases := []struct {
		pair string
		sep  byte
		want string
	}{
		{"BTCUSDT", '-', "BTC-USDT"},
		{"BTCUSDT", '_', "BTC_USDT"},
		{"ETHUSDC", '-', "ETH-USDC"},
		{"BTC-USDT", '-', "BTC-USDT"}, // 已含分隔符，原样返回
		{"", '-', ""},
		{"UNKNOWNXYZ", '-', "UNKNOWNXYZ"}, // 无已知计价后缀，原样返回
		{"USDTUSDT", '-', "USDT-USDT"},    // USDT 作基础币，按后缀 USDT 切分
	}
	for _, c := range cases {
		if got := SymbolWithSeparator(c.pair, c.sep); got != c.want {
			t.Errorf("SymbolWithSeparator(%q, %q) = %q, want %q", c.pair, c.sep, c.want, got)
		}
	}
}
