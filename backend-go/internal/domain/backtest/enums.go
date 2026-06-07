package backtest

type BacktestTaskStatus string

const (
	TaskStatusPending   BacktestTaskStatus = "Pending"
	TaskStatusRunning   BacktestTaskStatus = "Running"
	TaskStatusCompleted BacktestTaskStatus = "Completed"
	TaskStatusFailed    BacktestTaskStatus = "Failed"
	TaskStatusCancelled BacktestTaskStatus = "Cancelled"
)

type BacktestPhase string

const (
	PhaseQueued       BacktestPhase = "Queued"
	PhaseFetchingData BacktestPhase = "FetchingData"
	PhaseRunning      BacktestPhase = "Running"
)
