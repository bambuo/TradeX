package worker

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/infra/crypto"
	"tradex/internal/infra/exchange"
)

func TestFormatPair(t *testing.T) {
	cases := []struct {
		cur  string
		typ  domain.ExchangeType
		want string
	}{
		{"BTC", domain.ExchangeTypeBinance, "BTCUSDT"},
		{"BTC", domain.ExchangeTypeBybit, "BTCUSDT"},
		{"BTC", domain.ExchangeTypeHTX, "BTCUSDT"},
		{"BTC", domain.ExchangeTypeOKX, "BTC-USDT"},
		{"BTC", domain.ExchangeTypeGate, "BTC_USDT"},
	}
	for _, c := range cases {
		if got := formatPair(c.cur, c.typ); got != c.want {
			t.Errorf("formatPair(%q,%s)=%q want %q", c.cur, c.typ, got, c.want)
		}
	}
}

// ─── fakes ───

type fakeClient struct {
	balances map[string]decimal.Decimal
	orders   map[string][]exchange.ExchangeOrderDTO
	errPair  string // 该交易对查询时返回错误，用于验证隔离
}

func (c *fakeClient) Type() domain.ExchangeType { return domain.ExchangeTypeBinance }
func (c *fakeClient) GetAssetBalances(context.Context) (map[string]decimal.Decimal, error) {
	return c.balances, nil
}
func (c *fakeClient) GetOrderHistoryByPair(_ context.Context, pair string, _ int) ([]exchange.ExchangeOrderDTO, error) {
	if pair == c.errPair {
		return nil, context.DeadlineExceeded
	}
	return c.orders[pair], nil
}
func (c *fakeClient) GetOpenOrders(context.Context) ([]exchange.ExchangeOrderDTO, error) {
	return nil, nil
}
func (c *fakeClient) GetOrder(context.Context, string, string) (exchange.OrderResult, error) {
	return exchange.OrderResult{}, nil
}
func (c *fakeClient) GetOrderByClientOrderID(context.Context, string, string) (exchange.OrderResult, error) {
	return exchange.OrderResult{}, nil
}
func (c *fakeClient) PlaceOrder(context.Context, exchange.OrderRequest) (exchange.OrderResult, error) {
	return exchange.OrderResult{}, nil
}
func (c *fakeClient) GetOrderBook(context.Context, string, int) (exchange.OrderBook, error) {
	return exchange.OrderBook{}, nil
}
func (c *fakeClient) GetPairRules(context.Context) ([]exchange.PairRule, error) {
	return nil, nil
}

type fakeFactory struct{ client exchange.Client }

func (f *fakeFactory) CreateClient(domain.ExchangeType, string, string, *string) (exchange.Client, error) {
	return f.client, nil
}

type fakeExchangeRepo struct{ list []*domain.Exchange }

func (r *fakeExchangeRepo) GetByID(context.Context, uuid.UUID) (*domain.Exchange, error) {
	return nil, nil
}
func (r *fakeExchangeRepo) GetAllEnabled(context.Context) ([]*domain.Exchange, error) {
	return r.list, nil
}

type fakeHistoryRepo struct {
	upserted []*domain.ExchangeOrderHistory
}

func (r *fakeHistoryRepo) UpsertMany(_ context.Context, orders []*domain.ExchangeOrderHistory) error {
	r.upserted = append(r.upserted, orders...)
	return nil
}

func TestExchangeOrderSync_SyncOne(t *testing.T) {
	enc, err := crypto.NewService("dG6aBuGmi/4y19MilGpiY5eEMAdm7KWkfwKTzPFlzaw=")
	if err != nil {
		t.Fatal(err)
	}
	encKey, _ := enc.Encrypt("api")
	encSecret, _ := enc.Encrypt("secret")

	client := &fakeClient{
		balances: map[string]decimal.Decimal{
			"BTC":  decimal.RequireFromString("1"),
			"ETH":  decimal.RequireFromString("2"),
			"USDT": decimal.RequireFromString("100"), // 应被过滤
		},
		orders: map[string][]exchange.ExchangeOrderDTO{
			"BTCUSDT": {{Pair: "BTCUSDT", Side: "Buy", Type: "Market", Status: "Filled", ExchangeOrderID: "1"}},
		},
		errPair: "ETHUSDT", // ETH 查询失败，应被隔离而不影响 BTC 入库
	}
	histRepo := &fakeHistoryRepo{}
	svc := NewExchangeOrderSync(
		&fakeExchangeRepo{},
		histRepo,
		enc,
		&fakeFactory{client: client},
		zerolog.Nop(),
	)

	ex := &domain.Exchange{
		ID:                 uuid.New(),
		Name:               "test",
		Type:               domain.ExchangeTypeBinance,
		APIKeyEncrypted:    encKey,
		SecretKeyEncrypted: encSecret,
		Status:             domain.ExchangeStatusEnabled,
	}
	if err := svc.syncOne(context.Background(), ex); err != nil {
		t.Fatalf("syncOne: %v", err)
	}

	if len(histRepo.upserted) != 1 {
		t.Fatalf("应只入库 1 条（BTC 成功，ETH 隔离，USDT 过滤），实际 %d", len(histRepo.upserted))
	}
	got := histRepo.upserted[0]
	if got.ExchangeID != ex.ID || got.Pair != "BTCUSDT" || got.ExchangeOrderID != "1" {
		t.Fatalf("入库记录错误: %+v", got)
	}
	if got.SyncedAt.IsZero() {
		t.Fatal("SyncedAt 应被设置")
	}
}
