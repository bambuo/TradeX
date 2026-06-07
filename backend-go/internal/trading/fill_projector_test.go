package trading

import (
	"context"
	"testing"

	"github.com/google/uuid"
	"github.com/rs/zerolog"

	"tradex/internal/domain"
)

func filledBuy(strategyID uuid.UUID, qty, quote string) *domain.Order {
	o := domain.NewAutoOrder(uuid.New(), uuid.New(), "BTCUSDT", domain.OrderSideBuy, dn(quote), strategyID, nil)
	o.Quantity = dn(qty)
	_ = o.RecordFill(dn(qty), dn("0"), nil, nil) // → Filled
	return o
}

func TestFillProjector_BuyOpensPositionAndIsIdempotent(t *testing.T) {
	ctx := context.Background()
	posRepo := newFakePositionRepo()
	ordRepo := newFakeOrderRepo()
	bus := &recordingBus{}
	fp := NewFillProjector(posRepo, ordRepo, bus, zerolog.Nop())

	order := filledBuy(uuid.New(), "2", "200")

	// 首次投影 → 开一条持仓，回链 PositionID，发 PositionUpdated 事件
	if err := fp.ProjectFilled(ctx, order, dn("100")); err != nil {
		t.Fatal(err)
	}
	if len(posRepo.added) != 1 {
		t.Fatalf("应开 1 条持仓, got %d", len(posRepo.added))
	}
	pos := posRepo.added[0]
	if !pos.EntryPrice.Equal(dn("100")) || !pos.Quantity.Equal(dn("2")) {
		t.Fatalf("持仓数据错误: %+v", pos)
	}
	if order.PositionID == nil || *order.PositionID != pos.ID {
		t.Fatal("订单未回链 PositionID")
	}
	if len(bus.events) != 1 {
		t.Fatalf("应发 1 个事件, got %d", len(bus.events))
	}

	// 再次投影（对账重复路径）→ 幂等跳过，不再开仓
	if err := fp.ProjectFilled(ctx, order, dn("100")); err != nil {
		t.Fatal(err)
	}
	if len(posRepo.added) != 1 {
		t.Fatalf("幂等应仍为 1 条持仓, got %d", len(posRepo.added))
	}
}

func TestFillProjector_DirectedSellClosesPosition(t *testing.T) {
	ctx := context.Background()
	posRepo := newFakePositionRepo()
	ordRepo := newFakeOrderRepo()
	bus := &recordingBus{}
	fp := NewFillProjector(posRepo, ordRepo, bus, zerolog.Nop())

	pos := domain.OpenPosition(uuid.New(), uuid.New(), uuid.New(), "BTCUSDT", dn("1"), dn("100"))
	posRepo.byID[pos.ID] = pos

	sell := domain.NewAutoOrder(pos.TraderID, pos.ExchangeID, "BTCUSDT", domain.OrderSideSell, dn("0"), pos.StrategyID, &pos.ID)
	sell.Quantity = dn("1")
	_ = sell.RecordFill(dn("1"), dn("0"), nil, nil)

	if err := fp.ProjectFilled(ctx, sell, dn("120")); err != nil {
		t.Fatal(err)
	}
	if pos.Status != domain.PositionStatusClosed {
		t.Fatalf("持仓应被关闭, status=%s", pos.Status)
	}
	if !pos.RealizedPnl.Equal(dn("20")) { // (120-100)*1
		t.Fatalf("已实现盈亏应为 20, got %s", pos.RealizedPnl)
	}
}

func TestFillProjector_FIFOSellClosesOldest(t *testing.T) {
	ctx := context.Background()
	posRepo := newFakePositionRepo()
	ordRepo := newFakeOrderRepo()
	bus := &recordingBus{}
	fp := NewFillProjector(posRepo, ordRepo, bus, zerolog.Nop())

	strat := uuid.New()
	p1 := domain.OpenPosition(uuid.New(), uuid.New(), strat, "BTCUSDT", dn("1"), dn("100"))
	p2 := domain.OpenPosition(p1.TraderID, p1.ExchangeID, strat, "BTCUSDT", dn("1"), dn("110"))
	posRepo.openByStratPair[strat.String()+"|BTCUSDT"] = []*domain.Position{p1, p2}

	sell := domain.NewAutoOrder(p1.TraderID, p1.ExchangeID, "BTCUSDT", domain.OrderSideSell, dn("0"), strat, nil)
	sell.Quantity = dn("1")
	_ = sell.RecordFill(dn("1"), dn("0"), nil, nil)

	if err := fp.ProjectFilled(ctx, sell, dn("120")); err != nil {
		t.Fatal(err)
	}
	if p1.Status != domain.PositionStatusClosed {
		t.Fatal("应先平最旧的 p1")
	}
	if p2.Status != domain.PositionStatusOpen {
		t.Fatal("p2 应仍开仓（成交量仅覆盖 1）")
	}
}
