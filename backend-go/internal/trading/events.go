// Package trading 是从 C# TradeX.Trading 移植的实盘交易共享层
// （执行/对账/风控/事件总线/指标）。
package trading

import (
	"time"

	"github.com/google/uuid"
	"github.com/shopspring/decimal"
)

// 领域事件载荷里的 decimal 必须序列化为 JSON 数字（如 1.5），与 C# System.Text.Json
// 对 decimal 的处理一致，否则 C# API 端消费者反序列化失败。shopspring/decimal 默认输出
// 带引号的字符串，这里全局切换为不带引号的数字形式。
func init() {
	decimal.MarshalJSONWithoutQuotes = true
}

const eventsNamespace = "TradeX.Trading.Events."

// OrphanOrderDetectedPayload 交易所存在但本地缺失的孤儿订单告警。
type OrphanOrderDetectedPayload struct {
	ExchangeID      uuid.UUID       `json:"exchangeId"`
	ExchangeType    string          `json:"exchangeType"`
	Pair            string          `json:"pair"`
	ExchangeOrderID string          `json:"exchangeOrderId"`
	Side            string          `json:"side"`
	Type            string          `json:"type"`
	Price           decimal.Decimal `json:"price"`
	Quantity        decimal.Decimal `json:"quantity"`
	DetectedAt      time.Time       `json:"detectedAt"`
}

func (OrphanOrderDetectedPayload) EventType() string {
	return eventsNamespace + "OrphanOrderDetectedPayload"
}

// PositionDriftDetectedPayload 持仓漂移告警。Drift = LocalQuantity - ExchangeQuantity。
type PositionDriftDetectedPayload struct {
	ExchangeID       uuid.UUID       `json:"exchangeId"`
	ExchangeType     string          `json:"exchangeType"`
	TraderID         *uuid.UUID      `json:"traderId"`
	Asset            string          `json:"asset"`
	LocalQuantity    decimal.Decimal `json:"localQuantity"`
	ExchangeQuantity decimal.Decimal `json:"exchangeQuantity"`
	Drift            decimal.Decimal `json:"drift"`
	DriftPercent     decimal.Decimal `json:"driftPercent"`
	Severity         string          `json:"severity"`
	DetectedAt       time.Time       `json:"detectedAt"`
}

func (PositionDriftDetectedPayload) EventType() string {
	return eventsNamespace + "PositionDriftDetectedPayload"
}

// PositionUpdatedPayload 持仓开/平/刷新通知。
type PositionUpdatedPayload struct {
	PositionID    uuid.UUID       `json:"positionId"`
	TraderID      uuid.UUID       `json:"traderId"`
	ExchangeID    uuid.UUID       `json:"exchangeId"`
	StrategyID    uuid.UUID       `json:"strategyId"`
	Pair          string          `json:"pair"`
	Quantity      decimal.Decimal `json:"quantity"`
	EntryPrice    decimal.Decimal `json:"entryPrice"`
	UnrealizedPnl decimal.Decimal `json:"unrealizedPnl"`
	RealizedPnl   decimal.Decimal `json:"realizedPnl"`
	Status        string          `json:"status"`
	UpdatedAt     time.Time       `json:"updatedAt"`
}

func (PositionUpdatedPayload) EventType() string {
	return eventsNamespace + "PositionUpdatedPayload"
}

// OrderPlacedPayload 下单（买/卖）通知。供 Stage 4 策略消费者使用。
type OrderPlacedPayload struct {
	OrderID     uuid.UUID       `json:"orderId"`
	TraderID    uuid.UUID       `json:"traderId"`
	ExchangeID  uuid.UUID       `json:"exchangeId"`
	StrategyID  *uuid.UUID      `json:"strategyId"`
	Pair        string          `json:"pair"`
	Side        string          `json:"side"`
	Type        string          `json:"type"`
	Status      string          `json:"status"`
	Quantity    decimal.Decimal `json:"quantity"`
	PlacedAtUtc time.Time       `json:"placedAtUtc"`
}

func (OrderPlacedPayload) EventType() string {
	return eventsNamespace + "OrderPlacedPayload"
}

// RiskAlertPayload 风控告警。供 Stage 4 策略消费者使用。
type RiskAlertPayload struct {
	AlertID        uuid.UUID  `json:"alertId"`
	Level          string     `json:"level"`
	Category       string     `json:"category"`
	TraderID       uuid.UUID  `json:"traderId"`
	StrategyID     *uuid.UUID `json:"strategyId"`
	Message        string     `json:"message"`
	TriggeredAtUtc time.Time  `json:"triggeredAtUtc"`
}

func (RiskAlertPayload) EventType() string {
	return eventsNamespace + "RiskAlertPayload"
}

// KillSwitchActivatedPayload Kill Switch 激活通知。
type KillSwitchActivatedPayload struct {
	Reason               string     `json:"reason"`
	ActorUserID          *uuid.UUID `json:"actorUserId"`
	ActivatedAtUtc       time.Time  `json:"activatedAtUtc"`
	DisabledBindingCount int        `json:"disabledBindingCount"`
}

func (KillSwitchActivatedPayload) EventType() string {
	return eventsNamespace + "KillSwitchActivatedPayload"
}

// KillSwitchDeactivatedPayload Kill Switch 解除通知。
type KillSwitchDeactivatedPayload struct {
	Reason           string     `json:"reason"`
	ActorUserID      *uuid.UUID `json:"actorUserId"`
	DeactivatedAtUtc time.Time  `json:"deactivatedAtUtc"`
}

func (KillSwitchDeactivatedPayload) EventType() string {
	return eventsNamespace + "KillSwitchDeactivatedPayload"
}
