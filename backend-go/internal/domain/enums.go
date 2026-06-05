package domain

type OrderSide string

const (
	OrderSideBuy  OrderSide = "Buy"
	OrderSideSell OrderSide = "Sell"
)

type OrderType string

const (
	OrderTypeMarket    OrderType = "Market"
	OrderTypeLimit     OrderType = "Limit"
	OrderTypeStopLimit OrderType = "StopLimit"
)

type OrderStatus string

const (
	OrderStatusPending         OrderStatus = "Pending"
	OrderStatusPartiallyFilled OrderStatus = "PartiallyFilled"
	OrderStatusFilled          OrderStatus = "Filled"
	OrderStatusCancelled       OrderStatus = "Cancelled"
	OrderStatusFailed          OrderStatus = "Failed"
)

type PositionStatus string

const (
	PositionStatusOpen   PositionStatus = "Open"
	PositionStatusClosed PositionStatus = "Closed"
)

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
