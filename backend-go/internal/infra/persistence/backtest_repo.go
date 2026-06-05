package storage

import (
	"context"
	"encoding/json"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"

	"github.com/tradex/backend-go/internal/domain"
	"github.com/tradex/backend-go/internal/infra/persistence/ent"
	"github.com/tradex/backend-go/internal/infra/persistence/ent/backtestklineanalysis"
	"github.com/tradex/backend-go/internal/infra/persistence/ent/backtesttask"
)

type backtestRepo struct {
	client *ent.Client
}

func NewBacktestRepo(client *ent.Client) domain.BacktestRepository {
	return &backtestRepo{client: client}
}

func (r *backtestRepo) CreateTask(ctx context.Context, task *domain.BacktestTask) error {
	_, err := r.client.BacktestTask.Create().
		SetID(task.ID).
		SetStrategyID(task.StrategyID).
		SetStrategyName(task.StrategyName).
		SetCreatedBy(task.CreatedBy).
		SetExchangeID(task.ExchangeID).
		SetPair(task.Pair).
		SetTimeframe(task.Timeframe).
		SetInitialCapital(f64(task.InitialCapital)).
		SetNillablePositionSize(f64Ptr(task.PositionSize)).
		SetFeeRate(f64(task.FeeRate)).
		SetStartAt(task.StartAt).
		SetEndAt(task.EndAt).
		SetStatus(backtesttask.Status(task.Status)).
		Save(ctx)
	return err
}

func (r *backtestRepo) GetTask(ctx context.Context, id uuid.UUID) (*domain.BacktestTask, error) {
	row, err := r.client.BacktestTask.Query().
		Where(backtesttask.ID(id)).
		Only(ctx)
	if err != nil {
		return nil, err
	}
	return rowToTask(row), nil
}

func (r *backtestRepo) UpdateTaskStatus(ctx context.Context, id uuid.UUID, status domain.BacktestTaskStatus, phase *domain.BacktestPhase) error {
	upd := r.client.BacktestTask.Update().Where(backtesttask.ID(id)).
		SetStatus(backtesttask.Status(status))
	if phase != nil {
		upd.SetPhase(backtesttask.Phase(*phase))
	}
	_, err := upd.Save(ctx)
	return err
}

func (r *backtestRepo) UpdateTaskProgress(ctx context.Context, id uuid.UUID, progress int) error {
	_, err := r.client.BacktestTask.Update().Where(backtesttask.ID(id)).
		SetProgress(progress).
		Save(ctx)
	return err
}

func (r *backtestRepo) ListTasks(ctx context.Context, filter domain.TaskFilter) ([]*domain.BacktestTask, int, error) {
	query := r.client.BacktestTask.Query()
	if filter.Status != nil {
		query = query.Where(backtesttask.StatusEQ(backtesttask.Status(*filter.Status)))
	}
	if filter.Pair != nil {
		query = query.Where(backtesttask.Pair(*filter.Pair))
	}
	total, err := query.Count(ctx)
	if err != nil {
		return nil, 0, err
	}
	page := filter.Page
	if page < 1 {
		page = 1
	}
	pageSize := filter.PageSize
	if pageSize < 1 {
		pageSize = 20
	}
	rows, err := query.
		Order(ent.Desc(backtesttask.FieldCreatedAt)).
		Offset((page - 1) * pageSize).
		Limit(pageSize).
		All(ctx)
	if err != nil {
		return nil, 0, err
	}
	tasks := make([]*domain.BacktestTask, len(rows))
	for i, row := range rows {
		tasks[i] = rowToTask(row)
	}
	return tasks, total, nil
}

func (r *backtestRepo) SaveResult(ctx context.Context, taskID uuid.UUID, result *domain.BacktestResult, trades []domain.BacktestTrade) error {
	details, err := json.Marshal(trades)
	if err != nil {
		return err
	}
	task, err := r.client.BacktestTask.Get(ctx, taskID)
	if err != nil {
		return err
	}
	_, err = r.client.BacktestResult.Create().
		SetTask(task).
		SetStrategyName(result.StrategyName).
		SetFinalValue(f64(result.FinalValue)).
		SetTotalReturnPercent(f64(result.TotalReturnPercent)).
		SetAnnualizedReturnPercent(f64(result.AnnualizedReturnPercent)).
		SetMaxDrawdownPercent(f64(result.MaxDrawdownPercent)).
		SetWinRate(f64(result.WinRate)).
		SetSharpeRatio(f64(result.SharpeRatio)).
		SetProfitLossRatio(f64(result.ProfitLossRatio)).
		SetTotalTrades(result.TotalTrades).
		SetDetails(details).
		Save(ctx)
	return err
}

func (r *backtestRepo) SaveAnalysisBatch(ctx context.Context, taskID uuid.UUID, analysis []domain.BacktestKlineAnalysis) error {
	builders := make([]*ent.BacktestKlineAnalysisCreate, len(analysis))
	for i, a := range analysis {
		builders[i] = r.client.BacktestKlineAnalysis.Create().
			SetTaskID(taskID).
			SetKlineIndex(a.KlineIndex).
			SetTimestamp(a.Timestamp).
			SetOpen(f64(a.Open)).
			SetHigh(f64(a.High)).
			SetLow(f64(a.Low)).
			SetClose(f64(a.Close)).
			SetVolume(f64(a.Volume)).
			SetIndicatorValues(a.IndicatorValues).
			SetEntryConditionResult(a.EntryConditionResult).
			SetExitConditionResult(a.ExitConditionResult).
			SetInPosition(a.InPosition).
			SetAction(a.Action).
			SetPositionValue(f64(a.PositionValue)).
			SetPositionPnl(f64(a.PositionPnl))
	}
	_, err := r.client.BacktestKlineAnalysis.CreateBulk(builders...).Save(ctx)
	return err
}

func (r *backtestRepo) GetResult(ctx context.Context, taskID uuid.UUID) (*domain.BacktestResult, []domain.BacktestTrade, error) {
	task, err := r.client.BacktestTask.Get(ctx, taskID)
	if err != nil {
		return nil, nil, err
	}
	row, err := task.QueryResult().Only(ctx)
	if err != nil {
		return nil, nil, err
	}

	var trades []domain.BacktestTrade
	if len(row.Details) > 0 {
		if err := json.Unmarshal(row.Details, &trades); err != nil {
			return nil, nil, err
		}
	}

	result := &domain.BacktestResult{
		FinalValue:              dec(row.FinalValue),
		TotalReturnPercent:      dec(row.TotalReturnPercent),
		AnnualizedReturnPercent: dec(row.AnnualizedReturnPercent),
		MaxDrawdownPercent:      dec(row.MaxDrawdownPercent),
		WinRate:                 dec(row.WinRate),
		SharpeRatio:             dec(row.SharpeRatio),
		ProfitLossRatio:         dec(row.ProfitLossRatio),
		TotalTrades:             row.TotalTrades,
	}

	return result, trades, nil
}

func (r *backtestRepo) GetAnalysis(ctx context.Context, taskID uuid.UUID, cursor, limit int) ([]domain.BacktestKlineAnalysis, error) {
	if limit < 1 {
		limit = 100
	}
	rows, err := r.client.BacktestKlineAnalysis.Query().
		Where(backtestklineanalysis.TaskID(taskID)).
		Order(ent.Asc(backtestklineanalysis.FieldKlineIndex)).
		Limit(limit).
		All(ctx)
	if err != nil {
		return nil, err
	}
	result := make([]domain.BacktestKlineAnalysis, len(rows))
	for i, row := range rows {
		result[i] = *rowToAnalysis(row)
	}
	return result, nil
}

func (r *backtestRepo) GetStrategy(ctx context.Context, id uuid.UUID) (*domain.Strategy, error) {
	row, err := r.client.Strategy.Get(ctx, id)
	if err != nil {
		return nil, err
	}
	s := &domain.Strategy{
		ID:         row.ID,
		Name:       row.Name,
		ExchangeID: row.ExchangeID,
		Pair:       row.Pair,
		Timeframe:  row.Timeframe,
		IsActive:   row.IsActive,
	}
	if row.EntryCondition != "" {
		s.EntryCondition = json.RawMessage(row.EntryCondition)
	}
	if row.ExitCondition != "" {
		s.ExitCondition = json.RawMessage(row.ExitCondition)
	}
	if row.ExecutionRule != "" {
		s.ExecutionRule = json.RawMessage(row.ExecutionRule)
	}
	return s, nil
}

func (r *backtestRepo) GetAnalysisCount(ctx context.Context, taskID uuid.UUID) (int, error) {
	return r.client.BacktestKlineAnalysis.Query().
		Where(backtestklineanalysis.TaskID(taskID)).
		Count(ctx)
}

func (r *backtestRepo) GetPendingTasks(ctx context.Context) ([]*domain.BacktestTask, error) {
	rows, err := r.client.BacktestTask.Query().
		Where(backtesttask.StatusEQ(backtesttask.StatusPending)).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return rowsToTasks(rows), nil
}

func (r *backtestRepo) GetRunningTasks(ctx context.Context) ([]*domain.BacktestTask, error) {
	rows, err := r.client.BacktestTask.Query().
		Where(backtesttask.StatusEQ(backtesttask.StatusRunning)).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return rowsToTasks(rows), nil
}

func rowsToTasks(rows []*ent.BacktestTask) []*domain.BacktestTask {
	tasks := make([]*domain.BacktestTask, len(rows))
	for i, row := range rows {
		tasks[i] = rowToTask(row)
	}
	return tasks
}

func rowToTask(row *ent.BacktestTask) *domain.BacktestTask {
	task := &domain.BacktestTask{
		ID:             row.ID,
		StrategyID:     row.StrategyID,
		StrategyName:   row.StrategyName,
		CreatedBy:      row.CreatedBy,
		ExchangeID:     row.ExchangeID,
		Pair:           row.Pair,
		Timeframe:      row.Timeframe,
		InitialCapital: dec(row.InitialCapital),
		FeeRate:        dec(row.FeeRate),
		StartAt:        row.StartAt,
		EndAt:          row.EndAt,
		Status:         domain.BacktestTaskStatus(row.Status),
		Progress:       row.Progress,
		CreatedAt:      row.CreatedAt,
		UpdatedAt:      row.UpdatedAt,
	}
	if row.PositionSize != nil {
		v := dec(*row.PositionSize)
		task.PositionSize = &v
	}
	if row.CompletedAt != nil {
		task.CompletedAt = row.CompletedAt
	}
	if row.Phase != "" {
		p := domain.BacktestPhase(row.Phase)
		task.Phase = &p
	}
	if row.ErrorMessage != nil {
		task.ErrorMessage = row.ErrorMessage
	}
	return task
}

func rowToAnalysis(row *ent.BacktestKlineAnalysis) *domain.BacktestKlineAnalysis {
	a := &domain.BacktestKlineAnalysis{
		KlineIndex:           row.KlineIndex,
		Timestamp:            row.Timestamp,
		Open:                 dec(row.Open),
		High:                 dec(row.High),
		Low:                  dec(row.Low),
		Close:                dec(row.Close),
		Volume:               dec(row.Volume),
		IndicatorValues:      make(map[string]float64),
		EntryConditionResult: make(map[string]any),
		ExitConditionResult:  make(map[string]any),
		InPosition:           row.InPosition,
		Action:               row.Action,
		PositionValue:        dec(row.PositionValue),
		PositionPnl:          dec(row.PositionPnl),
	}
	if row.IndicatorValues != nil {
		a.IndicatorValues = row.IndicatorValues
	}
	if row.EntryConditionResult != nil {
		a.EntryConditionResult = row.EntryConditionResult
	}
	if row.ExitConditionResult != nil {
		a.ExitConditionResult = row.ExitConditionResult
	}
	return a
}

func f64(d decimal.Decimal) float64 {
	v, _ := d.Float64()
	return v
}

func f64Ptr(d *decimal.Decimal) *float64 {
	if d == nil {
		return nil
	}
	v, _ := d.Float64()
	return &v
}

func dec(f float64) decimal.Decimal {
	return decimal.NewFromFloat(f)
}
