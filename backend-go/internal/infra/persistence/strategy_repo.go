package persistence

import (
	"context"
	"encoding/json"

	"github.com/google/uuid"

	"tradex/internal/domain"
	"tradex/internal/infra/ent"
	"tradex/internal/infra/ent/strategy"
)

type strategyRepo struct {
	client *ent.Client
}

func NewStrategyRepo(client *ent.Client) domain.StrategyRepository {
	return &strategyRepo{client: client}
}

func (r *strategyRepo) GetStrategy(ctx context.Context, id uuid.UUID) (*domain.Strategy, error) {
	row, err := r.client.Strategy.Query().
		Where(strategy.IDEQ(id)).
		Only(ctx)
	if err != nil {
		if ent.IsNotFound(err) {
			return nil, nil
		}
		return nil, err
	}
	return mapStrategy(row), nil
}

func mapStrategy(s *ent.Strategy) *domain.Strategy {
	return &domain.Strategy{
		ID:             s.ID,
		Name:           s.Name,
		EntryCondition: json.RawMessage(s.EntryCondition),
		ExitCondition:  json.RawMessage(s.ExitCondition),
		ExecutionRule:  json.RawMessage(s.ExecutionRule),
		Version:        s.Version,
		CreatedBy:      s.CreatedBy,
		CreatedAt:      s.CreatedAt,
		UpdatedAt:      s.UpdatedAt,
	}
}
