package persistence

import (
	"context"
	"time"

	"github.com/google/uuid"

	"tradex/internal/domain"
	"tradex/internal/infra/ent"
	"tradex/internal/infra/ent/position"
)

type positionRepo struct {
	client *ent.Client
}

// NewPositionRepo 构造持仓仓储。
func NewPositionRepo(client *ent.Client) domain.PositionRepository {
	return &positionRepo{client: client}
}

func (r *positionRepo) GetByID(ctx context.Context, id uuid.UUID) (*domain.Position, error) {
	row, err := r.client.Position.Get(ctx, id)
	if err != nil {
		if ent.IsNotFound(err) {
			return nil, nil
		}
		return nil, err
	}
	return mapPosition(row), nil
}

func (r *positionRepo) GetAllOpen(ctx context.Context) ([]*domain.Position, error) {
	rows, err := r.client.Position.Query().
		Where(position.StatusEQ(string(domain.PositionStatusOpen))).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return mapPositions(rows), nil
}

func (r *positionRepo) GetByStrategyID(ctx context.Context, strategyID uuid.UUID) ([]*domain.Position, error) {
	rows, err := r.client.Position.Query().
		Where(position.StrategyID(strategyID)).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return mapPositions(rows), nil
}

func (r *positionRepo) GetByOpeningOrderID(ctx context.Context, openingOrderID uuid.UUID) (*domain.Position, error) {
	row, err := r.client.Position.Query().
		Where(position.OpeningOrderID(openingOrderID)).
		First(ctx)
	if err != nil {
		if ent.IsNotFound(err) {
			return nil, nil
		}
		return nil, err
	}
	return mapPosition(row), nil
}

func (r *positionRepo) GetOpenByStrategyAndPair(ctx context.Context, strategyID uuid.UUID, pair string) ([]*domain.Position, error) {
	rows, err := r.client.Position.Query().
		Where(
			position.StrategyID(strategyID),
			position.Pair(pair),
			position.StatusEQ(string(domain.PositionStatusOpen)),
		).
		Order(ent.Asc(position.FieldOpenedAtUtc)).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return mapPositions(rows), nil
}

func (r *positionRepo) GetOpenByTraderID(ctx context.Context, traderID uuid.UUID) ([]*domain.Position, error) {
	rows, err := r.client.Position.Query().
		Where(position.TraderID(traderID), position.StatusEQ(string(domain.PositionStatusOpen))).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return mapPositions(rows), nil
}

func (r *positionRepo) GetClosedByTraderIDSince(ctx context.Context, traderID uuid.UUID, since time.Time) ([]*domain.Position, error) {
	rows, err := r.client.Position.Query().
		Where(
			position.TraderID(traderID),
			position.StatusEQ(string(domain.PositionStatusClosed)),
			position.ClosedAtUtcGTE(since),
		).
		Order(ent.Desc(position.FieldClosedAtUtc)).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return mapPositions(rows), nil
}

func (r *positionRepo) Add(ctx context.Context, p *domain.Position) error {
	return r.client.Position.Create().
		SetID(p.ID).
		SetTraderID(p.TraderID).
		SetExchangeID(p.ExchangeID).
		SetStrategyID(p.StrategyID).
		SetNillableOpeningOrderID(p.OpeningOrderID).
		SetPair(p.Pair).
		SetQuantity(f64(p.Quantity)).
		SetEntryPrice(f64(p.EntryPrice)).
		SetCurrentPrice(f64(p.CurrentPrice)).
		SetUnrealizedPnl(f64(p.UnrealizedPnl)).
		SetRealizedPnl(f64(p.RealizedPnl)).
		SetStatus(string(p.Status)).
		SetOpenedAtUtc(p.OpenedAtUtc).
		SetNillableClosedAtUtc(p.ClosedAtUtc).
		SetUpdatedAt(p.UpdatedAt).
		SetVersion(p.Version).
		Exec(ctx)
}

func (r *positionRepo) Update(ctx context.Context, p *domain.Position) error {
	return r.client.Position.UpdateOneID(p.ID).
		SetQuantity(f64(p.Quantity)).
		SetCurrentPrice(f64(p.CurrentPrice)).
		SetUnrealizedPnl(f64(p.UnrealizedPnl)).
		SetRealizedPnl(f64(p.RealizedPnl)).
		SetStatus(string(p.Status)).
		SetNillableClosedAtUtc(p.ClosedAtUtc).
		SetUpdatedAt(p.UpdatedAt).
		SetVersion(p.Version).
		Exec(ctx)
}

func mapPositions(rows []*ent.Position) []*domain.Position {
	out := make([]*domain.Position, 0, len(rows))
	for _, row := range rows {
		out = append(out, mapPosition(row))
	}
	return out
}

func mapPosition(e *ent.Position) *domain.Position {
	return &domain.Position{
		ID:             e.ID,
		TraderID:       e.TraderID,
		ExchangeID:     e.ExchangeID,
		StrategyID:     e.StrategyID,
		OpeningOrderID: e.OpeningOrderID,
		Pair:           e.Pair,
		Quantity:       dec(e.Quantity),
		EntryPrice:     dec(e.EntryPrice),
		CurrentPrice:   dec(e.CurrentPrice),
		UnrealizedPnl:  dec(e.UnrealizedPnl),
		RealizedPnl:    dec(e.RealizedPnl),
		Status:         domain.PositionStatus(e.Status),
		OpenedAtUtc:    e.OpenedAtUtc,
		ClosedAtUtc:    e.ClosedAtUtc,
		UpdatedAt:      e.UpdatedAt,
		Version:        e.Version,
	}
}
