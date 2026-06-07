package domain

import (
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

// ExchangeOrderHistory 定时从交易所拉取的历史订单记录，支撑本地分页查询。
// 通过 (ExchangeID, ExchangeOrderID) 唯一约束做 upsert 去重。
// 对应 C# TradeX.Core.Models.ExchangeOrderHistory。
type ExchangeOrderHistory struct {
	ID              uuid.UUID       `json:"id"`
	ExchangeID      uuid.UUID       `json:"exchangeId"`
	Pair            string          `json:"pair"`
	Side            OrderSide       `json:"side"`
	Type            OrderType       `json:"type"`
	Status          OrderStatus     `json:"status"`
	Price           decimal.Decimal `json:"price"`
	Quantity        decimal.Decimal `json:"quantity"`
	FilledQuantity  decimal.Decimal `json:"filledQuantity"`
	ExchangeOrderID string          `json:"exchangeOrderId"`
	PlacedAt        time.Time       `json:"placedAt"`
	SyncedAt        time.Time       `json:"syncedAt"`
}

func NewExchangeOrderHistory(
	exchangeID uuid.UUID, pair string, side OrderSide, orderType OrderType, status OrderStatus,
	price, quantity, filledQuantity decimal.Decimal,
	exchangeOrderID string, placedAt time.Time,
) *ExchangeOrderHistory {
	return &ExchangeOrderHistory{
		ID:              uuid.New(),
		ExchangeID:      exchangeID,
		Pair:            pair,
		Side:            side,
		Type:            orderType,
		Status:          status,
		Price:           price,
		Quantity:        quantity,
		FilledQuantity:  filledQuantity,
		ExchangeOrderID: exchangeOrderID,
		PlacedAt:        placedAt,
		SyncedAt:        time.Now(),
	}
}
