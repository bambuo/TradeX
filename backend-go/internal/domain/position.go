package domain

import (
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

type Position struct {
	ID              uuid.UUID
	StrategyID      uuid.UUID
	TraderID        uuid.UUID
	Pair            string
	Side            OrderSide
	Status          PositionStatus
	Quantity        decimal.Decimal
	EntryPrice      decimal.Decimal
	CurrentPrice    decimal.Decimal
	UnrealizedPnl   decimal.Decimal
	RealizedPnl     decimal.Decimal
	OpeningOrderID  uuid.UUID
	Version         int
	OpenedAt        time.Time
	ClosedAt        *time.Time
}

func (p *Position) Open(orderID uuid.UUID, price, quantity decimal.Decimal) {
	p.Status = PositionStatusOpen
	p.OpeningOrderID = orderID
	p.EntryPrice = price
	p.Quantity = quantity
	p.CurrentPrice = price
	p.OpenedAt = time.Now()
}

func (p *Position) UpdateMarketPrice(price decimal.Decimal) {
	p.CurrentPrice = price
	p.UnrealizedPnl = price.Sub(p.EntryPrice).Mul(p.Quantity)
}

func (p *Position) Close(price decimal.Decimal) decimal.Decimal {
	p.CurrentPrice = price
	p.RealizedPnl = price.Sub(p.EntryPrice).Mul(p.Quantity)
	p.Status = PositionStatusClosed
	now := time.Now()
	p.ClosedAt = &now
	return p.RealizedPnl
}
