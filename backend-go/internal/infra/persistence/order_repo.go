package persistence

import (
	"context"

	"github.com/google/uuid"

	"tradex/internal/domain"
	"tradex/internal/infra/ent"
	"tradex/internal/infra/ent/order"
)

type orderRepo struct {
	client *ent.Client
}

// NewOrderRepo 构造订单仓储。
func NewOrderRepo(client *ent.Client) domain.OrderRepository {
	return &orderRepo{client: client}
}

func (r *orderRepo) GetByID(ctx context.Context, id uuid.UUID) (*domain.Order, error) {
	row, err := r.client.Order.Get(ctx, id)
	if err != nil {
		if ent.IsNotFound(err) {
			return nil, nil
		}
		return nil, err
	}
	return mapOrder(row), nil
}

func (r *orderRepo) GetByExchangeOrderID(ctx context.Context, exchangeOrderID string) (*domain.Order, error) {
	row, err := r.client.Order.Query().
		Where(order.ExchangeOrderID(exchangeOrderID)).
		First(ctx)
	if err != nil {
		if ent.IsNotFound(err) {
			return nil, nil
		}
		return nil, err
	}
	return mapOrder(row), nil
}

func (r *orderRepo) GetPendingByExchange(ctx context.Context, exchangeID uuid.UUID) ([]*domain.Order, error) {
	rows, err := r.client.Order.Query().
		Where(order.ExchangeID(exchangeID), order.StatusEQ(string(domain.OrderStatusPending))).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return mapOrders(rows), nil
}

func (r *orderRepo) HasActiveBuy(ctx context.Context, strategyID uuid.UUID, pair string) (bool, error) {
	return r.client.Order.Query().
		Where(
			order.StrategyID(strategyID),
			order.Pair(pair),
			order.SideEQ(string(domain.OrderSideBuy)),
			order.StatusIn(string(domain.OrderStatusPending), string(domain.OrderStatusPartiallyFilled)),
		).
		Exist(ctx)
}

func (r *orderRepo) Add(ctx context.Context, o *domain.Order) error {
	c := r.client.Order.Create().
		SetID(o.ID).
		SetTraderID(o.TraderID).
		SetClientOrderID(o.ClientOrderID).
		SetExchangeID(o.ExchangeID).
		SetPair(o.Pair).
		SetSide(string(o.Side)).
		SetType(string(o.Type)).
		SetStatus(string(o.Status)).
		SetQuantity(f64(o.Quantity)).
		SetFilledQuantity(f64(o.FilledQuantity)).
		SetQuoteQuantity(f64(o.QuoteQuantity)).
		SetFee(f64(o.Fee)).
		SetIsManual(o.IsManual).
		SetPlacedAtUtc(o.PlacedAtUtc).
		SetUpdatedAt(o.UpdatedAt).
		SetVersion(o.Version).
		SetNillableStrategyID(o.StrategyID).
		SetNillablePositionID(o.PositionID).
		SetNillableFeeAsset(o.FeeAsset)
	if o.ExchangeOrderID != "" {
		c.SetExchangeOrderID(o.ExchangeOrderID)
	}
	if o.Price != nil {
		c.SetPrice(f64(*o.Price))
	}
	return c.Exec(ctx)
}

func (r *orderRepo) Update(ctx context.Context, o *domain.Order) error {
	u := r.client.Order.UpdateOneID(o.ID).
		SetStatus(string(o.Status)).
		SetFilledQuantity(f64(o.FilledQuantity)).
		SetFee(f64(o.Fee)).
		SetUpdatedAt(o.UpdatedAt).
		SetVersion(o.Version).
		SetNillablePositionID(o.PositionID).
		SetNillableFeeAsset(o.FeeAsset)
	if o.ExchangeOrderID != "" {
		u.SetExchangeOrderID(o.ExchangeOrderID)
	}
	if o.Price != nil {
		u.SetPrice(f64(*o.Price))
	}
	return u.Exec(ctx)
}

func mapOrders(rows []*ent.Order) []*domain.Order {
	out := make([]*domain.Order, 0, len(rows))
	for _, row := range rows {
		out = append(out, mapOrder(row))
	}
	return out
}

func mapOrder(e *ent.Order) *domain.Order {
	o := &domain.Order{
		ID:             e.ID,
		TraderID:       e.TraderID,
		ClientOrderID:  e.ClientOrderID,
		ExchangeID:     e.ExchangeID,
		StrategyID:     e.StrategyID,
		PositionID:     e.PositionID,
		Pair:           e.Pair,
		Side:           domain.OrderSide(e.Side),
		Type:           domain.OrderType(e.Type),
		Status:         domain.OrderStatus(e.Status),
		Quantity:       dec(e.Quantity),
		FilledQuantity: dec(e.FilledQuantity),
		QuoteQuantity:  dec(e.QuoteQuantity),
		Fee:            dec(e.Fee),
		FeeAsset:       e.FeeAsset,
		IsManual:       e.IsManual,
		PlacedAtUtc:    e.PlacedAtUtc,
		UpdatedAt:      e.UpdatedAt,
		Version:        e.Version,
	}
	if e.ExchangeOrderID != nil {
		o.ExchangeOrderID = *e.ExchangeOrderID
	}
	if e.Price != nil {
		p := dec(*e.Price)
		o.Price = &p
	}
	return o
}
