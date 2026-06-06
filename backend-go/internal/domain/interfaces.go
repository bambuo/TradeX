package domain

import "context"

// CancelNotifier publishes cross-process cancellation events.
type CancelNotifier interface {
	NotifyCancel(ctx context.Context, taskID string) error
}

// AnalysisStore provides live streaming of backtest K-line analysis data.
type AnalysisStore interface {
	Init(taskID string)
	Push(taskID string, item BacktestKlineAnalysis)
	Get(taskID string) []BacktestKlineAnalysis
	Count(taskID string) int
	Remove(taskID string)
	Subscribe(taskID string) (<-chan BacktestKlineAnalysis, bool)
	Exists(taskID string) bool
}
