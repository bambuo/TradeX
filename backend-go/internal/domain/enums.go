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

// ExchangeType 对应 C# TradeX.Core.Enums.ExchangeType（顺序一致，用于与持久化整型/字符串互通）。
type ExchangeType string

const (
	ExchangeTypeBinance ExchangeType = "Binance"
	ExchangeTypeOKX     ExchangeType = "OKX"
	ExchangeTypeGate    ExchangeType = "Gate"
	ExchangeTypeBybit   ExchangeType = "Bybit"
	ExchangeTypeHTX     ExchangeType = "HTX"
)

// ExchangeStatus 对应 C# ExchangeStatus。
type ExchangeStatus string

const (
	ExchangeStatusEnabled  ExchangeStatus = "Enabled"
	ExchangeStatusDisabled ExchangeStatus = "Disabled"
)

// BindingStatus 对应 C# TradeX.Core.Enums.BindingStatus。
type BindingStatus string

const (
	BindingStatusDraft       BindingStatus = "Draft"
	BindingStatusBacktesting BindingStatus = "Backtesting"
	BindingStatusPassed      BindingStatus = "Passed"
	BindingStatusActive      BindingStatus = "Active"
	BindingStatusDisabled    BindingStatus = "Disabled"
)
