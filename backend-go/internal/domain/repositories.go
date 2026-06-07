package domain

import (
	"context"
	"time"

	"github.com/google/uuid"
)

// ExchangeRepository 交易所配置仓储。Worker 仅用到只读方法，写方法供 API 进程实现同一接口时扩展。
// 对应 C# IExchangeRepository（此处只声明 Worker 链路所需子集）。
type ExchangeRepository interface {
	GetByID(ctx context.Context, id uuid.UUID) (*Exchange, error)
	GetAllEnabled(ctx context.Context) ([]*Exchange, error)
}

// StrategyBindingRepository 策略绑定仓储。
type StrategyBindingRepository interface {
	GetAllActive(ctx context.Context) ([]*StrategyBinding, error)
	UpdateRange(ctx context.Context, bindings []*StrategyBinding) error
}

// StrategyRepository 策略仓储（读取入场/出场/执行规则 JSON）。
type StrategyRepository interface {
	GetStrategy(ctx context.Context, id uuid.UUID) (*Strategy, error)
}

// ExchangeOrderHistoryRepository 交易所历史订单仓储。
type ExchangeOrderHistoryRepository interface {
	// UpsertMany 按 (ExchangeID, ExchangeOrderID) 唯一键插入或更新，重复则刷新可变字段。
	UpsertMany(ctx context.Context, orders []*ExchangeOrderHistory) error
}

// OrderRepository 订单仓储（对账/执行/投影所需子集）。对应 C# IOrderRepository。
type OrderRepository interface {
	GetByID(ctx context.Context, id uuid.UUID) (*Order, error)
	GetByExchangeOrderID(ctx context.Context, exchangeOrderID string) (*Order, error)
	GetPendingByExchange(ctx context.Context, exchangeID uuid.UUID) ([]*Order, error)
	// HasActiveBuy 某策略某交易对是否存在在途买单（Pending/PartiallyFilled），用于入场幂等闸跨重启兜底。
	HasActiveBuy(ctx context.Context, strategyID uuid.UUID, pair string) (bool, error)
	Add(ctx context.Context, order *Order) error
	Update(ctx context.Context, order *Order) error
}

// PositionRepository 持仓仓储（对账/投影/评估所需子集）。对应 C# IPositionRepository。
type PositionRepository interface {
	GetByID(ctx context.Context, id uuid.UUID) (*Position, error)
	GetAllOpen(ctx context.Context) ([]*Position, error)
	GetByStrategyID(ctx context.Context, strategyID uuid.UUID) ([]*Position, error)
	// GetByOpeningOrderID 凭开仓订单 Id 反查持仓，用于"成交→持仓"投影的幂等判重。
	GetByOpeningOrderID(ctx context.Context, openingOrderID uuid.UUID) (*Position, error)
	// GetOpenByStrategyAndPair 取某策略某交易对的 Open 持仓，按开仓时间升序（供 FIFO 平仓）。
	GetOpenByStrategyAndPair(ctx context.Context, strategyID uuid.UUID, pair string) ([]*Position, error)
	// GetOpenByTraderID 取某 trader 所有 Open 持仓（风控上下文）。
	GetOpenByTraderID(ctx context.Context, traderID uuid.UUID) ([]*Position, error)
	// GetClosedByTraderIDSince 取某 trader 自 since 起平仓的持仓，按平仓时间倒序（风控当日盈亏/连亏）。
	GetClosedByTraderIDSince(ctx context.Context, traderID uuid.UUID, since time.Time) ([]*Position, error)
	Add(ctx context.Context, position *Position) error
	Update(ctx context.Context, position *Position) error
}
