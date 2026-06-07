package domain

import (
	"context"

	"github.com/google/uuid"
)

type BacktestRepository interface {
	CreateTask(ctx context.Context, task *BacktestTask) error
	GetTask(ctx context.Context, id uuid.UUID) (*BacktestTask, error)
	UpdateTaskStatus(ctx context.Context, id uuid.UUID, status BacktestTaskStatus, phase *BacktestPhase) error
	ListTasks(ctx context.Context, filter TaskFilter) ([]*BacktestTask, int, error)
	SaveResult(ctx context.Context, taskID uuid.UUID, result *BacktestResult, trades []BacktestTrade) error
	GetResult(ctx context.Context, taskID uuid.UUID) (*BacktestResult, []BacktestTrade, error)
	SaveAnalysisBatch(ctx context.Context, taskID uuid.UUID, analysis []BacktestKlineAnalysis) error
	GetAnalysis(ctx context.Context, taskID uuid.UUID, cursor, limit int) ([]BacktestKlineAnalysis, error)
	GetAnalysisCount(ctx context.Context, taskID uuid.UUID) (int, error)
	GetPendingTasks(ctx context.Context) ([]*BacktestTask, error)
	GetRunningTasks(ctx context.Context) ([]*BacktestTask, error)
	ExecuteInTransaction(ctx context.Context, fn func(BacktestRepository) error) error
	TryAcquireTask(ctx context.Context, id uuid.UUID, fromStatus BacktestTaskStatus, phase BacktestPhase) (bool, error)
	StrategyRepository
}

type StrategyRepository interface {
	GetStrategy(ctx context.Context, id uuid.UUID) (*Strategy, error)
}

type TaskFilter struct {
	Status   *BacktestTaskStatus
	Pair     *string
	Page     int
	PageSize int
}
