package domain

import (
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

type Order struct {
	ID              uuid.UUID
	ClientOrderID   string
	ExchangeOrderID string
	StrategyID      uuid.UUID
	TraderID        uuid.UUID
	Pair            string
	Side            OrderSide
	Type            OrderType
	Status          OrderStatus
	Price           decimal.Decimal
	Quantity        decimal.Decimal
	QuoteQuantity   decimal.Decimal
	FilledQuantity  decimal.Decimal
	AvgFillPrice    decimal.Decimal
	Fee             decimal.Decimal
	CreatedAt       time.Time
	UpdatedAt       time.Time
}

func (o *Order) Fill(price, quantity, fee decimal.Decimal) {
	o.Status = OrderStatusFilled
	o.AvgFillPrice = price
	o.FilledQuantity = o.FilledQuantity.Add(quantity)
	o.Fee = o.Fee.Add(fee)
	o.UpdatedAt = time.Now()
}

func (o *Order) PartialFill(price, quantity, fee decimal.Decimal) {
	o.Status = OrderStatusPartiallyFilled
	o.AvgFillPrice = price
	o.FilledQuantity = o.FilledQuantity.Add(quantity)
	o.Fee = o.Fee.Add(fee)
	o.UpdatedAt = time.Now()
}

func (o *Order) Cancel() error {
	if o.Status == OrderStatusFilled {
		return ErrInvalidOperation
	}
	o.Status = OrderStatusCancelled
	o.UpdatedAt = time.Now()
	return nil
}

func (o *Order) MarkFailed(errMsg string) {
	o.Status = OrderStatusFailed
	o.UpdatedAt = time.Now()
}
