package service

import (
	"context"
	"fmt"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"

	"github.com/tradex/backend-go/internal/domain"
)

type CreateBacktestRequest struct {
	StrategyID     string
	ExchangeID     string
	Pair           string
	Timeframe      string
	InitialCapital float64
	PositionSize   *float64
	StartAt        string
	EndAt          string
	FeeRate        float64
}

type BacktestService struct {
	repo domain.BacktestRepository
}

func NewBacktestService(repo domain.BacktestRepository) *BacktestService {
	return &BacktestService{repo: repo}
}

func (s *BacktestService) CreateTask(ctx context.Context, req CreateBacktestRequest) (*domain.BacktestTask, error) {
	strategyID, err := uuid.Parse(req.StrategyID)
	if err != nil {
		return nil, fmt.Errorf("%w: invalid strategy_id: %s", domain.ErrInvalidInput, req.StrategyID)
	}

	// validate strategy exists
	strategy, err := s.repo.GetStrategy(ctx, strategyID)
	if err != nil {
		return nil, fmt.Errorf("%w: strategy not found: %s", domain.ErrInvalidInput, req.StrategyID)
	}
	if !strategy.IsActive {
		return nil, fmt.Errorf("%w: strategy is inactive", domain.ErrInvalidInput)
	}

	startAt, err := time.Parse(time.RFC3339, req.StartAt)
	if err != nil {
		return nil, fmt.Errorf("%w: invalid start_at, use RFC3339", domain.ErrInvalidInput)
	}

	endAt, err := time.Parse(time.RFC3339, req.EndAt)
	if err != nil {
		return nil, fmt.Errorf("%w: invalid end_at, use RFC3339", domain.ErrInvalidInput)
	}

	if endAt.Before(startAt) {
		return nil, fmt.Errorf("%w: end_at must be after start_at", domain.ErrInvalidInput)
	}

	task := &domain.BacktestTask{
		ID:             uuid.New(),
		StrategyID:     strategyID,
		StrategyName:   strategy.Name,
		CreatedBy:      uuid.Nil,
		ExchangeID:     req.ExchangeID,
		Pair:           req.Pair,
		Timeframe:      req.Timeframe,
		InitialCapital: decimal.NewFromFloat(req.InitialCapital),
		FeeRate:        decimal.NewFromFloat(req.FeeRate),
		StartAt:        startAt,
		EndAt:          endAt,
		Status:         domain.TaskStatusPending,
		CreatedAt:      time.Now(),
		UpdatedAt:      time.Now(),
	}
	if req.PositionSize != nil {
		v := decimal.NewFromFloat(*req.PositionSize)
		task.PositionSize = &v
	}

	if err := s.repo.CreateTask(ctx, task); err != nil {
		return nil, fmt.Errorf("create task: %w", err)
	}

	_ = strategy // strategy loaded and validated
	return task, nil
}

func (s *BacktestService) GetTask(ctx context.Context, id uuid.UUID) (*domain.BacktestTask, error) {
	return s.repo.GetTask(ctx, id)
}

func (s *BacktestService) ListTasks(ctx context.Context, filter domain.TaskFilter) ([]*domain.BacktestTask, int, error) {
	return s.repo.ListTasks(ctx, filter)
}

func (s *BacktestService) CancelTask(ctx context.Context, id uuid.UUID) error {
	task, err := s.repo.GetTask(ctx, id)
	if err != nil {
		return err
	}
	if task.Status == domain.TaskStatusCompleted || task.Status == domain.TaskStatusFailed {
		return fmt.Errorf("%w: task already finished", domain.ErrConflict)
	}
	return s.repo.UpdateTaskStatus(ctx, id, domain.TaskStatusCancelled, nil)
}

func (s *BacktestService) GetResult(ctx context.Context, id uuid.UUID) (*domain.BacktestResult, []domain.BacktestTrade, error) {
	return s.repo.GetResult(ctx, id)
}

func (s *BacktestService) GetAnalysis(ctx context.Context, id uuid.UUID, cursor, limit int) ([]domain.BacktestKlineAnalysis, error) {
	return s.repo.GetAnalysis(ctx, id, cursor, limit)
}

func (s *BacktestService) GetAnalysisCount(ctx context.Context, id uuid.UUID) (int, error) {
	return s.repo.GetAnalysisCount(ctx, id)
}
