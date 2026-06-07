package persistence

import (
	"context"
	"encoding/json"
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	bt "tradex/internal/domain/backtest"
	"tradex/internal/infra/ent"
	"tradex/internal/infra/ent/backtestklineanalysis"
	"tradex/internal/infra/ent/backtestresult"
	"tradex/internal/infra/ent/backtesttask"
)

type backtestRepo struct {
	client *ent.Client
}

func NewBacktestRepo(client *ent.Client) bt.BacktestRepository {
	return &backtestRepo{client: client}
}

func (r *backtestRepo) CreateTask(ctx context.Context, task *bt.BacktestTask) error {
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
		SetStartAt(task.StartAt).
		SetEndAt(task.EndAt).
		SetStatus(backtesttask.Status(task.Status)).
		Save(ctx)
	return err
}

func (r *backtestRepo) GetTask(ctx context.Context, id uuid.UUID) (*bt.BacktestTask, error) {
	row, err := r.client.BacktestTask.Query().
		Where(backtesttask.ID(id)).
		Only(ctx)
	if err != nil {
		return nil, err
	}
	return rowToTask(row), nil
}

func (r *backtestRepo) UpdateTaskStatus(ctx context.Context, id uuid.UUID, status bt.BacktestTaskStatus, phase *bt.BacktestPhase) error {
	upd := r.client.BacktestTask.Update().Where(backtesttask.ID(id)).
		SetStatus(backtesttask.Status(status))
	if phase != nil {
		upd.SetPhase(backtesttask.Phase(*phase))
	} else {
		upd.ClearPhase()
	}
	now := time.Now()
	switch status {
	case bt.TaskStatusCompleted, bt.TaskStatusFailed, bt.TaskStatusCancelled:
		upd.SetCompletedAt(now)
	}
	_, err := upd.Save(ctx)
	return err
}

func (r *backtestRepo) ListTasks(ctx context.Context, filter bt.TaskFilter) ([]*bt.BacktestTask, int, error) {
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
	tasks := make([]*bt.BacktestTask, len(rows))
	for i, row := range rows {
		tasks[i] = rowToTask(row)
	}
	return tasks, total, nil
}

func (r *backtestRepo) SaveResult(ctx context.Context, taskID uuid.UUID, result *bt.BacktestResult, trades []bt.BacktestTrade) error {
	details, err := json.Marshal(trades)
	if err != nil {
		return err
	}
	_, err = r.client.BacktestResult.Create().
		SetTaskID(taskID).
		SetStrategyName(result.StrategyName).
		SetPair(result.Pair).
		SetTimeframe(result.Timeframe).
		SetStartAt(result.StartAt).
		SetEndAt(result.EndAt).
		SetInitialCapital(f64(result.InitialCapital)).
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

func (r *backtestRepo) SaveAnalysisBatch(ctx context.Context, taskID uuid.UUID, analysis []bt.BacktestKlineAnalysis) error {
	builders := make([]*ent.BacktestKlineAnalysisCreate, len(analysis))
	for i, a := range analysis {
		builders[i] = r.client.BacktestKlineAnalysis.Create().
			SetTaskID(taskID).
			SetIndex(a.KlineIndex).
			SetTimestamp(a.Timestamp).
			SetOpen(f64(a.Open)).
			SetHigh(f64(a.High)).
			SetLow(f64(a.Low)).
			SetClose(f64(a.Close)).
			SetVolume(f64(a.Volume)).
			SetIndicatorValues(a.IndicatorValues).
			SetNillableEntryConditionResult(boolPtr(a.EntryConditionResult)).
			SetNillableExitConditionResult(boolPtr(a.ExitConditionResult)).
			SetInPosition(a.InPosition).
			SetAction(a.Action).
			SetNillableAvgEntryPrice(f64Ptr(a.AvgEntryPrice)).
			SetNillablePositionQuantity(f64Ptr(a.PositionQuantity)).
			SetNillablePositionCost(f64Ptr(a.PositionCost)).
			SetNillablePositionValue(f64Ptr(a.PositionValue)).
			SetNillablePositionPnl(f64Ptr(a.PositionPnl)).
			SetNillablePositionPnlPercent(f64Ptr(a.PositionPnlPercent))
	}
	_, err := r.client.BacktestKlineAnalysis.CreateBulk(builders...).Save(ctx)
	return err
}

func (r *backtestRepo) ExecuteInTransaction(ctx context.Context, fn func(bt.BacktestRepository) error) error {
	tx, err := r.client.Tx(ctx)
	if err != nil {
		return err
	}
	defer tx.Rollback()

	txRepo := &backtestRepo{client: tx.Client()}
	if err := fn(txRepo); err != nil {
		return err
	}
	return tx.Commit()
}

func (r *backtestRepo) GetResult(ctx context.Context, taskID uuid.UUID) (*bt.BacktestResult, []bt.BacktestTrade, error) {
	row, err := r.client.BacktestResult.Query().
		Where(backtestresult.TaskID(taskID)).
		Only(ctx)
	if err != nil {
		return nil, nil, err
	}

	var trades []bt.BacktestTrade
	if len(row.Details) > 0 {
		if err := json.Unmarshal(row.Details, &trades); err != nil {
			return nil, nil, err
		}
	}

	result := &bt.BacktestResult{
		StrategyName:            row.StrategyName,
		Pair:                    row.Pair,
		Timeframe:               row.Timeframe,
		StartAt:                 row.StartAt,
		EndAt:                   row.EndAt,
		InitialCapital:          dec(row.InitialCapital),
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

func (r *backtestRepo) GetAnalysis(ctx context.Context, taskID uuid.UUID, cursor, limit int) ([]bt.BacktestKlineAnalysis, error) {
	if limit < 1 {
		limit = 100
	}
	rows, err := r.client.BacktestKlineAnalysis.Query().
		Where(backtestklineanalysis.TaskID(taskID)).
		Order(ent.Asc(backtestklineanalysis.FieldIndex)).
		Offset(cursor).
		Limit(limit).
		All(ctx)
	if err != nil {
		return nil, err
	}
	result := make([]bt.BacktestKlineAnalysis, len(rows))
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
		ID:        row.ID,
		Name:      row.Name,
		Version:   row.Version,
		CreatedBy: row.CreatedBy,
		CreatedAt: row.CreatedAt,
		UpdatedAt: row.UpdatedAt,
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

func (r *backtestRepo) GetPendingTasks(ctx context.Context) ([]*bt.BacktestTask, error) {
	rows, err := r.client.BacktestTask.Query().
		Where(backtesttask.StatusEQ(backtesttask.StatusPending)).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return rowsToTasks(rows), nil
}

func (r *backtestRepo) GetRunningTasks(ctx context.Context) ([]*bt.BacktestTask, error) {
	rows, err := r.client.BacktestTask.Query().
		Where(backtesttask.StatusEQ(backtesttask.StatusRunning)).
		All(ctx)
	if err != nil {
		return nil, err
	}
	return rowsToTasks(rows), nil
}

func (r *backtestRepo) TryAcquireTask(ctx context.Context, id uuid.UUID, fromStatus bt.BacktestTaskStatus, phase bt.BacktestPhase) (bool, error) {
	n, err := r.client.BacktestTask.Update().
		Where(backtesttask.ID(id), backtesttask.StatusEQ(backtesttask.Status(fromStatus))).
		SetStatus(backtesttask.StatusRunning).
		SetPhase(backtesttask.Phase(phase)).
		Save(ctx)
	if err != nil {
		return false, err
	}
	return n == 1, nil
}

func rowsToTasks(rows []*ent.BacktestTask) []*bt.BacktestTask {
	tasks := make([]*bt.BacktestTask, len(rows))
	for i, row := range rows {
		tasks[i] = rowToTask(row)
	}
	return tasks
}

func rowToTask(row *ent.BacktestTask) *bt.BacktestTask {
	task := &bt.BacktestTask{
		ID:             row.ID,
		StrategyID:     row.StrategyID,
		StrategyName:   row.StrategyName,
		CreatedBy:      row.CreatedBy,
		ExchangeID:     row.ExchangeID,
		Pair:           row.Pair,
		Timeframe:      row.Timeframe,
		InitialCapital: dec(row.InitialCapital),
		StartAt:        row.StartAt,
		EndAt:          row.EndAt,
		Status:         bt.BacktestTaskStatus(row.Status),
		CreatedAt:      row.CreatedAt,
	}
	if row.PositionSize != nil {
		v := dec(*row.PositionSize)
		task.PositionSize = &v
	}
	if row.CompletedAt != nil {
		task.CompletedAt = row.CompletedAt
	}
	if row.Phase != "" {
		p := bt.BacktestPhase(row.Phase)
		task.Phase = &p
	}
	return task
}

func rowToAnalysis(row *ent.BacktestKlineAnalysis) *bt.BacktestKlineAnalysis {
	a := &bt.BacktestKlineAnalysis{
		KlineIndex:      row.Index,
		Timestamp:       row.Timestamp,
		Open:            dec(row.Open),
		High:            dec(row.High),
		Low:             dec(row.Low),
		Close:           dec(row.Close),
		Volume:          dec(row.Volume),
		IndicatorValues: make(map[string]float64),
		InPosition:      row.InPosition,
		Action:          row.Action,
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
	if row.AvgEntryPrice != nil {
		v := dec(*row.AvgEntryPrice)
		a.AvgEntryPrice = &v
	}
	if row.PositionQuantity != nil {
		v := dec(*row.PositionQuantity)
		a.PositionQuantity = &v
	}
	if row.PositionCost != nil {
		v := dec(*row.PositionCost)
		a.PositionCost = &v
	}
	if row.PositionValue != nil {
		v := dec(*row.PositionValue)
		a.PositionValue = &v
	}
	if row.PositionPnl != nil {
		v := dec(*row.PositionPnl)
		a.PositionPnl = &v
	}
	if row.PositionPnlPercent != nil {
		v := dec(*row.PositionPnlPercent)
		a.PositionPnlPercent = &v
	}
	return a
}

func f64(d decimal.Decimal) float64 {
	v, _ := d.Float64()
	return v
}

func boolPtr(b *bool) *bool {
	if b == nil {
		return nil
	}
	return b
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
