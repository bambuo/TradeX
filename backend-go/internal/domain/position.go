package domain

import (
	"fmt"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

// Position 持仓聚合根。字段与状态机对齐 C# TradeX.Core.Models.Position 及 positions 表列。
// 状态机：Open ──UpdateMarketPrice──► Open；──Close──► Closed（终态）。
type Position struct {
	ID             uuid.UUID       `json:"id"`
	TraderID       uuid.UUID       `json:"traderId"`
	ExchangeID     uuid.UUID       `json:"exchangeId"`
	StrategyID     uuid.UUID       `json:"strategyId"`
	OpeningOrderID *uuid.UUID      `json:"openingOrderId,omitempty"`
	Pair           string          `json:"pair"`
	Quantity       decimal.Decimal `json:"quantity"`
	EntryPrice     decimal.Decimal `json:"entryPrice"`
	CurrentPrice   decimal.Decimal `json:"currentPrice"`
	UnrealizedPnl  decimal.Decimal `json:"unrealizedPnl"`
	RealizedPnl    decimal.Decimal `json:"realizedPnl"`
	Status         PositionStatus  `json:"status"`
	OpenedAtUtc    time.Time       `json:"openedAtUtc"`
	ClosedAtUtc    *time.Time      `json:"closedAtUtc,omitempty"`
	UpdatedAt      time.Time       `json:"updatedAt"`
	Version        uuid.UUID       `json:"version"`
}

// OpenPosition 工厂方法：开仓（对应 C# Position.Open）。
func OpenPosition(traderID, exchangeID, strategyID uuid.UUID, pair string, quantity, entryPrice decimal.Decimal) *Position {
	now := time.Now().UTC()
	return &Position{
		ID:           uuid.New(),
		TraderID:     traderID,
		ExchangeID:   exchangeID,
		StrategyID:   strategyID,
		Pair:         pair,
		Quantity:     quantity,
		EntryPrice:   entryPrice,
		CurrentPrice: entryPrice,
		Status:       PositionStatusOpen,
		OpenedAtUtc:  now,
		UpdatedAt:    now,
		Version:      uuid.New(),
	}
}

// UpdateMarketPrice 用当前市价刷新持仓（仅 Open 生效）。UnrealizedPnl=(price-entry)*qty。
func (p *Position) UpdateMarketPrice(currentPrice decimal.Decimal) error {
	if currentPrice.IsNegative() {
		return fmt.Errorf("价格不能为负")
	}
	if p.Status != PositionStatusOpen {
		return fmt.Errorf("持仓 %s 已 %s，不能更新价格", p.ID, p.Status)
	}
	p.CurrentPrice = currentPrice
	p.UnrealizedPnl = currentPrice.Sub(p.EntryPrice).Mul(p.Quantity)
	p.UpdatedAt = time.Now().UTC()
	return nil
}

// Close 关闭持仓，记录实现盈亏（Open → Closed）。
func (p *Position) Close(exitPrice decimal.Decimal) error {
	if p.Status != PositionStatusOpen {
		return fmt.Errorf("持仓 %s 已 %s，不能再次关闭", p.ID, p.Status)
	}
	p.RealizedPnl = exitPrice.Sub(p.EntryPrice).Mul(p.Quantity)
	p.UnrealizedPnl = decimal.Zero
	p.CurrentPrice = exitPrice
	p.Status = PositionStatusClosed
	now := time.Now().UTC()
	p.ClosedAtUtc = &now
	p.UpdatedAt = now
	return nil
}
