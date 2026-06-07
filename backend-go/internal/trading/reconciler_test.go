package trading

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/rs/zerolog"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

func TestResolveBaseAsset(t *testing.T) {
	quotes := []string{"USDT", "USDC", "USD", "BTC"} // 已按长度降序
	cases := map[string]string{
		"BTCUSDT":  "BTC",
		"BTC-USDT": "BTC",
		"BTC_USDT": "BTC",
		"ETHUSDC":  "ETH",
		"ETHBTC":   "ETH",
		"UNKNOWN":  "",
		"":         "",
	}
	for pair, want := range cases {
		if got := ResolveBaseAsset(pair, quotes); got != want {
			t.Errorf("ResolveBaseAsset(%q)=%q want %q", pair, got, want)
		}
	}
}

func enabledExchange() *domain.Exchange {
	return &domain.Exchange{
		ID: uuid.New(), Name: "bn", Type: domain.ExchangeTypeBinance,
		APIKeyEncrypted: "k", SecretKeyEncrypted: "s", Status: domain.ExchangeStatusEnabled,
	}
}

func TestPositionReconciler_CriticalDriftWhenLocalExceedsActual(t *testing.T) {
	ctx := context.Background()
	ex := enabledExchange()
	exRepo := &fakeExchangeRepo{list: map[uuid.UUID]*domain.Exchange{ex.ID: ex}}

	pos := domain.OpenPosition(uuid.New(), ex.ID, uuid.New(), "BTCUSDT", dn("1"), dn("100"))
	posRepo := newFakePositionRepo()
	posRepo.allOpen = []*domain.Position{pos}

	bus := &recordingBus{}
	metrics, _ := NewMetrics()
	provider := NewExchangeClientProvider(&fakeFactory{client: &fakeExClient{balances: balanceMap("BTC", "0.5")}}, fakeDecryptor{}, zerolog.Nop())
	pr := NewPositionReconciler(exRepo, posRepo, provider, bus, metrics, DefaultRiskSettings(), zerolog.Nop())

	n, err := pr.ReconcilePositions(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if n != 1 {
		t.Fatalf("应检出 1 项漂移, got %d", n)
	}
	if len(bus.events) != 1 {
		t.Fatalf("应发 1 个漂移事件, got %d", len(bus.events))
	}
	drift, ok := bus.events[0].(PositionDriftDetectedPayload)
	if !ok || drift.Severity != "Critical" {
		t.Fatalf("应为 Critical 漂移事件: %+v", bus.events[0])
	}
}

func TestOrderReconciler_DetectsOrphan(t *testing.T) {
	ctx := context.Background()
	ex := enabledExchange()
	exRepo := &fakeExchangeRepo{list: map[uuid.UUID]*domain.Exchange{ex.ID: ex}}
	ordRepo := newFakeOrderRepo() // 本地无任何订单

	client := &fakeExClient{openOrders: []exchange.ExchangeOrderDTO{
		{Pair: "BTCUSDT", Side: "Buy", Type: "Limit", Status: "New", ExchangeOrderID: "999", Price: dn("50000"), Quantity: dn("0.1")},
	}}
	provider := NewExchangeClientProvider(&fakeFactory{client: client}, fakeDecryptor{}, zerolog.Nop())
	bus := &recordingBus{}
	fp := NewFillProjector(newFakePositionRepo(), ordRepo, bus, zerolog.Nop())
	or := NewOrderReconciler(exRepo, ordRepo, provider, bus, fp, DefaultRiskSettings(), zerolog.Nop())

	n, err := or.DetectOrphanOrders(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if n != 1 {
		t.Fatalf("应检出 1 笔孤儿, got %d", n)
	}
	if _, ok := bus.events[0].(OrphanOrderDetectedPayload); !ok {
		t.Fatalf("应发孤儿事件: %+v", bus.events[0])
	}
}

func TestOrderReconciler_RecoversFilledOrder(t *testing.T) {
	ctx := context.Background()
	ex := enabledExchange()
	exRepo := &fakeExchangeRepo{list: map[uuid.UUID]*domain.Exchange{ex.ID: ex}}

	// 一笔有 ExchangeOrderId 的 Pending 买单，Quantity=1
	sid := uuid.New()
	order := domain.NewAutoOrder(uuid.New(), ex.ID, "BTCUSDT", domain.OrderSideBuy, dn("100"), sid, nil)
	order.Quantity = dn("1")
	order.ExchangeOrderID = "111"
	ordRepo := newFakeOrderRepo()
	ordRepo.pending[ex.ID] = []*domain.Order{order}

	client := &fakeExClient{getOrder: func(_, _ string) (exchange.OrderResult, error) {
		return exchange.OrderResult{Success: true, ExchangeOrderID: "111", FilledQuantity: dn("1"), AvgPrice: dn("100")}, nil
	}}
	provider := NewExchangeClientProvider(&fakeFactory{client: client}, fakeDecryptor{}, zerolog.Nop())
	bus := &recordingBus{}
	posRepo := newFakePositionRepo()
	fp := NewFillProjector(posRepo, ordRepo, bus, zerolog.Nop())
	or := NewOrderReconciler(exRepo, ordRepo, provider, bus, fp, DefaultRiskSettings(), zerolog.Nop())

	if err := or.Reconcile(ctx); err != nil {
		t.Fatal(err)
	}
	if order.Status != domain.OrderStatusFilled {
		t.Fatalf("订单应被对账为 Filled, got %s", order.Status)
	}
	if len(posRepo.added) != 1 {
		t.Fatalf("成交应投影出 1 条持仓, got %d", len(posRepo.added))
	}
}
