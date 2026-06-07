package domain

import (
	"context"

	"github.com/google/uuid"
)

type StrategyRepository interface {
	GetStrategy(ctx context.Context, id uuid.UUID) (*Strategy, error)
}
