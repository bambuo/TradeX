package backtest

import (
	"context"
	"fmt"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"

	bt "tradex/internal/domain/backtest"
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

type Service struct {
	repo bt.BacktestRepository
}

func NewService(repo bt.BacktestRepository) *Service {
	return &Service{repo: repo}
}

func (s *Service) CreateTask(ctx context.Context, req CreateBacktestRequest) (*bt.BacktestTask, error) {
	strategyID, err := uuid.Parse(req.StrategyID)
	if err != nil {
		return nil, fmt.Errorf("%w: 无效的策略ID: %s", domain.ErrInvalidInput, req.StrategyID)
	}

	exchangeID, err := uuid.Parse(req.ExchangeID)
	if err != nil {
		return nil, fmt.Errorf("%w: 无效的交易所ID: %s", domain.ErrInvalidInput, req.ExchangeID)
	}

	strategy, err := s.repo.GetStrategy(ctx, strategyID)
	if err != nil {
		return nil, fmt.Errorf("%w: 策略不存在: %s", domain.ErrInvalidInput, req.StrategyID)
	}

	startAt, err := time.Parse(time.RFC3339, req.StartAt)
	if err != nil {
		return nil, fmt.Errorf("%w: 开始时间格式无效，请使用 RFC3339", domain.ErrInvalidInput)
	}

	endAt, err := time.Parse(time.RFC3339, req.EndAt)
	if err != nil {
		return nil, fmt.Errorf("%w: 结束时间格式无效，请使用 RFC3339", domain.ErrInvalidInput)
	}

	task := &bt.BacktestTask{
		ID:             uuid.New(),
		StrategyID:     strategyID,
		StrategyName:   strategy.Name,
		CreatedBy:      uuid.Nil,
		ExchangeID:     exchangeID,
		Pair:           req.Pair,
		Timeframe:      req.Timeframe,
		InitialCapital: decimal.NewFromFloat(req.InitialCapital),
		StartAt:        startAt,
		EndAt:          endAt,
		Status:         bt.TaskStatusPending,
		CreatedAt:      time.Now(),
	}
	if err := task.Validate(); err != nil {
		return nil, fmt.Errorf("%w: %w", domain.ErrInvalidInput, err)
	}
	if req.PositionSize != nil {
		v := decimal.NewFromFloat(*req.PositionSize)
		task.PositionSize = &v
	}

	if err := s.repo.CreateTask(ctx, task); err != nil {
		return nil, fmt.Errorf("create task: %w", err)
	}

	return task, nil
}

func (s *Service) GetTask(ctx context.Context, id uuid.UUID) (*bt.BacktestTask, error) {
	return s.repo.GetTask(ctx, id)
}

func (s *Service) ListTasks(ctx context.Context, filter bt.TaskFilter) ([]*bt.BacktestTask, int, error) {
	return s.repo.ListTasks(ctx, filter)
}

func (s *Service) CancelTask(ctx context.Context, id uuid.UUID) error {
	task, err := s.repo.GetTask(ctx, id)
	if err != nil {
		return err
	}
	if err := task.Cancel(); err != nil {
		return err
	}
	return s.repo.SaveTask(ctx, task)
}

func (s *Service) GetResult(ctx context.Context, id uuid.UUID) (*bt.BacktestResult, []bt.BacktestTrade, error) {
	task, err := s.repo.GetTask(ctx, id)
	if err != nil {
		return nil, nil, err
	}
	if task.Status != bt.TaskStatusCompleted {
		return nil, nil, fmt.Errorf("%w: 任务尚未完成", domain.ErrConflict)
	}
	return s.repo.GetResult(ctx, id)
}

func (s *Service) GetAnalysis(ctx context.Context, id uuid.UUID, cursor, limit int) ([]bt.BacktestKlineAnalysis, error) {
	return s.repo.GetAnalysis(ctx, id, cursor, limit)
}

func (s *Service) GetAnalysisCount(ctx context.Context, id uuid.UUID) (int, error) {
	return s.repo.GetAnalysisCount(ctx, id)
}
