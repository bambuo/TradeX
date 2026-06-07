package persistence

import (
	"context"

	"github.com/google/uuid"

	"tradex/internal/domain"
	"tradex/internal/infra/ent"
	"tradex/internal/infra/ent/exchange"
)

type exchangeRepo struct {
	client *ent.Client
}

// NewExchangeRepo 构造交易所配置仓储。
func NewExchangeRepo(client *ent.Client) domain.ExchangeRepository {
	return &exchangeRepo{client: client}
}

func (r *exchangeRepo) GetByID(ctx context.Context, id uuid.UUID) (*domain.Exchange, error) {
	row, err := r.client.Exchange.Get(ctx, id)
	if err != nil {
		if ent.IsNotFound(err) {
			return nil, nil
		}
		return nil, err
	}
	return mapExchange(row), nil
}

func (r *exchangeRepo) GetAllEnabled(ctx context.Context) ([]*domain.Exchange, error) {
	rows, err := r.client.Exchange.Query().
		Where(exchange.StatusEQ(string(domain.ExchangeStatusEnabled))).
		All(ctx)
	if err != nil {
		return nil, err
	}
	out := make([]*domain.Exchange, 0, len(rows))
	for _, row := range rows {
		out = append(out, mapExchange(row))
	}
	return out, nil
}

func mapExchange(e *ent.Exchange) *domain.Exchange {
	return &domain.Exchange{
		ID:                  e.ID,
		Name:                e.Name,
		Type:                domain.ExchangeType(e.Type),
		APIKeyEncrypted:     e.APIKeyEncrypted,
		SecretKeyEncrypted:  e.SecretKeyEncrypted,
		PassphraseEncrypted: e.PassphraseEncrypted,
		Status:              domain.ExchangeStatus(e.Status),
		LastTestedAt:        e.LastTestedAt,
		TestResult:          e.TestResult,
		CreatedBy:           e.CreatedBy,
		CreatedAt:           e.CreatedAt,
		UpdatedAt:           e.UpdatedAt,
	}
}
