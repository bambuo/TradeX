package domain

import (
	"fmt"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

// Order 订单聚合根。字段与状态机对齐 C# TradeX.Core.Models.Order 及 orders 表列。
//
// 状态机：Pending ──RecordFill──► PartiallyFilled/Filled；──MarkFailed──► Failed；
// ──MarkCancelled──► Cancelled。Filled/Failed/Cancelled 为终态。
type Order struct {
	ID              uuid.UUID        `json:"id"`
	TraderID        uuid.UUID        `json:"traderId"`
	ClientOrderID   uuid.UUID        `json:"clientOrderId"`
	ExchangeOrderID string           `json:"exchangeOrderId"`
	ExchangeID      uuid.UUID        `json:"exchangeId"`
	StrategyID      *uuid.UUID       `json:"strategyId,omitempty"`
	PositionID      *uuid.UUID       `json:"positionId,omitempty"`
	Pair            string           `json:"pair"`
	Side            OrderSide        `json:"side"`
	Type            OrderType        `json:"type"`
	Status          OrderStatus      `json:"status"`
	Price           *decimal.Decimal `json:"price,omitempty"`
	Quantity        decimal.Decimal  `json:"quantity"`
	FilledQuantity  decimal.Decimal  `json:"filledQuantity"`
	QuoteQuantity   decimal.Decimal  `json:"quoteQuantity"`
	Fee             decimal.Decimal  `json:"fee"`
	FeeAsset        *string          `json:"feeAsset,omitempty"`
	IsManual        bool             `json:"isManual"`
	PlacedAtUtc     time.Time        `json:"placedAtUtc"`
	UpdatedAt       time.Time        `json:"updatedAt"`
	Version         uuid.UUID        `json:"version"`
}

// NewAutoOrder 创建策略自动市价单（对应 C# Order.CreateAuto，下单事件由调用方显式发布）。
func NewAutoOrder(traderID, exchangeID uuid.UUID, pair string, side OrderSide, quoteQuantity decimal.Decimal, strategyID uuid.UUID, positionID *uuid.UUID) *Order {
	now := time.Now().UTC()
	sid := strategyID
	return &Order{
		ID:            uuid.New(),
		TraderID:      traderID,
		ClientOrderID: uuid.New(),
		ExchangeID:    exchangeID,
		StrategyID:    &sid,
		PositionID:    positionID,
		Pair:          pair,
		Side:          side,
		Type:          OrderTypeMarket,
		Status:        OrderStatusPending,
		Quantity:      decimal.Zero,
		QuoteQuantity: quoteQuantity,
		IsManual:      false,
		PlacedAtUtc:   now,
		UpdatedAt:     now,
		Version:       uuid.New(),
	}
}

var terminalOrderStatuses = map[OrderStatus]bool{
	OrderStatusFilled:    true,
	OrderStatusFailed:    true,
	OrderStatusCancelled: true,
}

// IsTerminal 报告订单是否已到终态。
func (o *Order) IsTerminal() bool { return terminalOrderStatuses[o.Status] }

// MarkPlaced 记录交易所已受理并返回订单号，状态保持 Pending。
func (o *Order) MarkPlaced(exchangeOrderID string) error {
	if exchangeOrderID == "" {
		return fmt.Errorf("ExchangeOrderID 不能为空")
	}
	if o.IsTerminal() {
		return fmt.Errorf("订单 %s 已是终态 %s，不能 MarkPlaced", o.ID, o.Status)
	}
	o.ExchangeOrderID = exchangeOrderID
	o.UpdatedAt = time.Now().UTC()
	return nil
}

// RecordFill 记录成交。filledQuantity>=Quantity 且 Quantity>0 → Filled；>0 → PartiallyFilled；否则 Pending。
// 对应 C# Order.RecordFill。exchangeOrderID/feeAsset 为 nil 时不覆盖。
func (o *Order) RecordFill(filledQuantity, fee decimal.Decimal, exchangeOrderID, feeAsset *string) error {
	if filledQuantity.IsNegative() {
		return fmt.Errorf("成交数量不能为负")
	}
	if o.IsTerminal() {
		return fmt.Errorf("订单 %s 已是终态 %s，不能 RecordFill", o.ID, o.Status)
	}
	if exchangeOrderID != nil {
		o.ExchangeOrderID = *exchangeOrderID
	}
	o.FilledQuantity = filledQuantity
	o.Fee = fee
	if feeAsset != nil {
		o.FeeAsset = feeAsset
	}
	switch {
	case filledQuantity.GreaterThanOrEqual(o.Quantity) && o.Quantity.IsPositive():
		o.Status = OrderStatusFilled
	case filledQuantity.IsPositive():
		o.Status = OrderStatusPartiallyFilled
	default:
		o.Status = OrderStatusPending
	}
	o.UpdatedAt = time.Now().UTC()
	return nil
}

// MarkFailed 标记下单失败（终态）。
func (o *Order) MarkFailed(_ string) error {
	if o.IsTerminal() {
		return fmt.Errorf("订单 %s 已是终态 %s，不能 MarkFailed", o.ID, o.Status)
	}
	o.Status = OrderStatusFailed
	o.UpdatedAt = time.Now().UTC()
	return nil
}

// MarkCancelled 标记已取消（终态）。
func (o *Order) MarkCancelled() error {
	if o.IsTerminal() {
		return fmt.Errorf("订单 %s 已是终态 %s，不能 MarkCancelled", o.ID, o.Status)
	}
	o.Status = OrderStatusCancelled
	o.UpdatedAt = time.Now().UTC()
	return nil
}
