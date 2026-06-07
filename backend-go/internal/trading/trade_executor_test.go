package trading

import (
	"context"
	"testing"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

func TestNormalizePair(t *testing.T) {
	tests := []struct{ in, want string }{
		{"BTCUSDT", "BTCUSDT"},
		{"BTC-USDT", "BTCUSDT"},
		{"BTC_USDT", "BTCUSDT"},
		{"btcusdt", "BTCUSDT"},
		{"BTC/USDT", "BTCUSDT"},
	}
	for _, tt := range tests {
		if got := normalizePair(tt.in); got != tt.want {
			t.Errorf("normalizePair(%q) = %q, want %q", tt.in, got, tt.want)
		}
	}
}

func TestPairRuleCache_Get(t *testing.T) {
	cache := newPairRuleCache()
	client := &fakeExClient{}

	// 首次调用：无缓存，调用 GetPairRules
	rule, err := cache.Get(context.Background(), client, "BTCUSDT")
	if err != nil {
		t.Fatal(err)
	}
	if rule != nil {
		t.Error("无规则时应返回 nil")
	}
}

func TestPairRuleCache_NormalizedLookup(t *testing.T) {
	cache := newPairRuleCache()
	client := &fakeExClientWithRules{}

	rule, err := cache.Get(context.Background(), client, "BTCUSDT")
	if err != nil {
		t.Fatal(err)
	}
	if rule == nil {
		t.Fatal("BTCUSDT 应找到规则")
	}
	if !rule.MinNotional.Equal(dn("10")) {
		t.Errorf("MinNotional = %s, want 10", rule.MinNotional.String())
	}

	// 不同格式也应该找到（归一化）
	rule2, _ := cache.Get(context.Background(), client, "BTC-USDT")
	if rule2 == nil {
		t.Error("BTC-USDT 归一化后应找到规则")
	}
}

type fakeExClientWithRules struct{}

func (c *fakeExClientWithRules) Type() domain.ExchangeType { return domain.ExchangeTypeBinance }
func (c *fakeExClientWithRules) GetAssetBalances(context.Context) (map[string]decimal.Decimal, error) {
	return nil, nil
}
func (c *fakeExClientWithRules) GetOrderHistoryByPair(context.Context, string, int) ([]exchange.ExchangeOrderDTO, error) {
	return nil, nil
}
func (c *fakeExClientWithRules) GetOpenOrders(context.Context) ([]exchange.ExchangeOrderDTO, error) {
	return nil, nil
}
func (c *fakeExClientWithRules) GetOrder(context.Context, string, string) (exchange.OrderResult, error) {
	return exchange.OrderResult{}, nil
}
func (c *fakeExClientWithRules) GetOrderByClientOrderID(context.Context, string, string) (exchange.OrderResult, error) {
	return exchange.OrderResult{Success: false, Error: "not_supported"}, nil
}
func (c *fakeExClientWithRules) PlaceOrder(context.Context, exchange.OrderRequest) (exchange.OrderResult, error) {
	return exchange.OrderResult{}, nil
}
func (c *fakeExClientWithRules) GetOrderBook(context.Context, string, int) (exchange.OrderBook, error) {
	return exchange.OrderBook{}, nil
}
func (c *fakeExClientWithRules) GetPairRules(context.Context) ([]exchange.PairRule, error) {
	return []exchange.PairRule{
		{Pair: "BTCUSDT", MinNotional: dn("10"), StepSize: dn("0.001"), MinQuantity: dn("0.001")},
		{Pair: "ETHUSDT", MinNotional: dn("10"), StepSize: dn("0.01"), MinQuantity: dn("0.01")},
	}, nil
}

func TestOrderBookSlippageGuard_Sufficient(t *testing.T) {
	guard := newOrderBookSlippageGuard()
	book := exchange.OrderBook{
		Asks: []exchange.OrderBookLevel{
			{Price: dn("100"), Quantity: dn("1")},
			{Price: dn("101"), Quantity: dn("2")},
			{Price: dn("102"), Quantity: dn("5")},
		},
		Bids: []exchange.OrderBookLevel{
			{Price: dn("99"), Quantity: dn("1")},
			{Price: dn("98"), Quantity: dn("2")},
		},
	}

	// 买单吃 1.5 个：100*1 + 101*0.5 = 100+50.5=150.5, avg=150.5/1.5=100.33, slip=0.33%
	est := guard.Estimate(book, domain.OrderSideBuy, dn("1.5"), dn("100"))
	if !est.Sufficient {
		t.Fatalf("深度足够: Reason=%s", est.Reason)
	}
	if est.SlippagePercent.LessThan(decimal.Zero) {
		t.Error("滑点不应为负")
	}
}

func TestOrderBookSlippageGuard_Insufficient(t *testing.T) {
	guard := newOrderBookSlippageGuard()
	book := exchange.OrderBook{
		Asks: []exchange.OrderBookLevel{
			{Price: dn("100"), Quantity: dn("1")},
		},
	}

	// 买单吃 10 个但只有 1 个可用
	est := guard.Estimate(book, domain.OrderSideBuy, dn("10"), dn("100"))
	if est.Sufficient {
		t.Fatal("深度不足时应返回 false")
	}
}

func TestOrderBookSlippageGuard_EmptyBook(t *testing.T) {
	guard := newOrderBookSlippageGuard()
	book := exchange.OrderBook{}

	est := guard.Estimate(book, domain.OrderSideBuy, dn("1"), dn("100"))
	if est.Sufficient {
		t.Fatal("空订单簿应返回深度不足")
	}
}

func TestOrderBookSlippageGuard_ZeroPrice(t *testing.T) {
	guard := newOrderBookSlippageGuard()
	book := exchange.OrderBook{
		Asks: []exchange.OrderBookLevel{{Price: dn("100"), Quantity: dn("1")}},
	}

	est := guard.Estimate(book, domain.OrderSideBuy, dn("1"), decimal.Zero)
	if !est.Sufficient {
		t.Fatal("参考价为 0 时应跳过滑点检查")
	}
}
