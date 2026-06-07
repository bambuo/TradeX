package persistence

import (
	"context"

	"tradex/internal/domain"
	"tradex/internal/infra/ent"
	"tradex/internal/infra/ent/strategybinding"
)

type strategyBindingRepo struct {
	client *ent.Client
}

// NewStrategyBindingRepo 构造策略绑定仓储。
func NewStrategyBindingRepo(client *ent.Client) domain.StrategyBindingRepository {
	return &strategyBindingRepo{client: client}
}

// GetAllActive 返回所有 Active 绑定。
func (r *strategyBindingRepo) GetAllActive(ctx context.Context) ([]*domain.StrategyBinding, error) {
	rows, err := r.client.StrategyBinding.Query().
		Where(strategybinding.StatusEQ(string(domain.BindingStatusActive))).
		All(ctx)
	if err != nil {
		return nil, err
	}
	out := make([]*domain.StrategyBinding, 0, len(rows))
	for _, row := range rows {
		out = append(out, mapBinding(row))
	}
	return out, nil
}

func (r *strategyBindingRepo) UpdateRange(ctx context.Context, bindings []*domain.StrategyBinding) error {
	for _, b := range bindings {
		if err := r.client.StrategyBinding.UpdateOneID(b.ID).
			SetStatus(string(b.Status)).
			SetUpdatedAt(b.UpdatedAt).
			Exec(ctx); err != nil {
			return err
		}
	}
	return nil
}

func mapBinding(b *ent.StrategyBinding) *domain.StrategyBinding {
	return &domain.StrategyBinding{
		ID:         b.ID,
		StrategyID: b.StrategyID,
		Name:       b.Name,
		TraderID:   b.TraderID,
		ExchangeID: b.ExchangeID,
		Pairs:      b.Pairs,
		Timeframe:  b.Timeframe,
		Status:     domain.BindingStatus(b.Status),
		CreatedBy:  b.CreatedBy,
		CreatedAt:  b.CreatedAt,
		UpdatedAt:  b.UpdatedAt,
	}
}
