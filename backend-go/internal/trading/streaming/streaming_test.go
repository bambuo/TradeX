package streaming

import (
	"context"
	"testing"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

// ─── fakes ───

type fakeBindingRepo struct{ list []*domain.StrategyBinding }

func (r *fakeBindingRepo) GetAllActive(context.Context) ([]*domain.StrategyBinding, error) {
	return r.list, nil
}
func (r *fakeBindingRepo) UpdateRange(context.Context, []*domain.StrategyBinding) error { return nil }

type fakeExchangeRepo struct{ t domain.ExchangeType }

func (r *fakeExchangeRepo) GetByID(_ context.Context, id uuid.UUID) (*domain.Exchange, error) {
	return &domain.Exchange{ID: id, Type: r.t, Status: domain.ExchangeStatusEnabled}, nil
}
func (r *fakeExchangeRepo) GetAllEnabled(context.Context) ([]*domain.Exchange, error) {
	return nil, nil
}

type fakeStreamClient struct{ candles []domain.Kline }

func (c *fakeStreamClient) SubscribeTrades(ctx context.Context, _ string, _ func(exchange.Trade)) error {
	<-ctx.Done()
	return ctx.Err()
}
func (c *fakeStreamClient) SubscribeKlines(ctx context.Context, _, _ string, onKline func(domain.Kline)) error {
	for _, cd := range c.candles {
		onKline(cd)
	}
	<-ctx.Done()
	return ctx.Err()
}

type fakeStreamFactory struct {
	client exchange.MarketDataStreamClient
}

func (f *fakeStreamFactory) CreatePublicClient(domain.ExchangeType) (exchange.MarketDataStreamClient, error) {
	return f.client, nil
}

func candleAt(sec int, closePx string) domain.Kline {
	return domain.Kline{
		Timestamp: time.Unix(int64(sec), 0).UTC(),
		Close:     decimal.RequireFromString(closePx),
	}
}

func TestKlineStreamManager_ClosesOnOpenTimeChange(t *testing.T) {
	exID := uuid.New()
	binding := &domain.StrategyBinding{ID: uuid.New(), ExchangeID: exID, Pairs: "BTCUSDT", Timeframe: "1m", Status: domain.BindingStatusActive}

	// 序列：t1(open) → t1dup(同 open，忽略) → t2(推 t1 收盘) → t3(推 t2 收盘)
	candles := []domain.Kline{
		candleAt(60, "100"),
		candleAt(60, "999"), // 同 OpenTime，应被忽略（保留首个快照 close=100）
		candleAt(120, "110"),
		candleAt(180, "120"),
	}
	client := &fakeStreamClient{candles: candles}

	out := make(chan KlineEvent, 8)
	m := NewKlineStreamManager(
		&fakeBindingRepo{list: []*domain.StrategyBinding{binding}},
		&fakeExchangeRepo{t: domain.ExchangeTypeBinance},
		&fakeStreamFactory{client: client},
		out,
		zerolog.Nop(),
	)

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	if err := m.Start(ctx); err != nil {
		t.Fatal(err)
	}
	defer m.Stop()

	// 应收到 2 个收盘事件：t1(close=100，忽略 dup)、t2(close=110)
	e1 := readKline(t, out)
	if e1.Kline.Timestamp.Unix() != 60 || !e1.Kline.Close.Equal(decimal.RequireFromString("100")) {
		t.Fatalf("第一个收盘事件应为 t1 close=100, got %+v", e1.Kline)
	}
	if e1.Pair != "BTCUSDT" || e1.Interval != "1m" || e1.ExchangeID != exID {
		t.Fatalf("事件元数据错误: %+v", e1)
	}
	e2 := readKline(t, out)
	if e2.Kline.Timestamp.Unix() != 120 || !e2.Kline.Close.Equal(decimal.RequireFromString("110")) {
		t.Fatalf("第二个收盘事件应为 t2 close=110, got %+v", e2.Kline)
	}
}

func readKline(t *testing.T, out chan KlineEvent) KlineEvent {
	t.Helper()
	select {
	case e := <-out:
		return e
	case <-time.After(2 * time.Second):
		t.Fatal("超时未收到 K 线收盘事件")
		return KlineEvent{}
	}
}

func TestTradeStreamManager_SubscriptionDiff(t *testing.T) {
	exID := uuid.New()
	repo := &fakeBindingRepo{list: []*domain.StrategyBinding{
		{ID: uuid.New(), ExchangeID: exID, Pairs: "BTCUSDT,ETHUSDT", Status: domain.BindingStatusActive},
	}}
	out := make(chan TradeEvent, 8)
	m := NewTradeStreamManager(repo, &fakeExchangeRepo{t: domain.ExchangeTypeBinance},
		&fakeStreamFactory{client: &fakeStreamClient{}}, out, zerolog.Nop())

	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	if err := m.Start(ctx); err != nil {
		t.Fatal(err)
	}
	defer m.Stop()

	m.mu.Lock()
	n := len(m.subs)
	m.mu.Unlock()
	if n != 2 {
		t.Fatalf("应建立 2 个订阅(BTCUSDT,ETHUSDT), got %d", n)
	}

	// 绑定缩减到 1 个交易对 → 刷新后应只剩 1 个订阅
	repo.list[0].Pairs = "BTCUSDT"
	if err := m.RefreshSubscriptions(ctx); err != nil {
		t.Fatal(err)
	}
	m.mu.Lock()
	n = len(m.subs)
	_, hasBTC := m.subs[tradeKey(exID, "BTCUSDT")]
	m.mu.Unlock()
	if n != 1 || !hasBTC {
		t.Fatalf("刷新后应只剩 BTCUSDT 订阅, got n=%d hasBTC=%v", n, hasBTC)
	}
}
