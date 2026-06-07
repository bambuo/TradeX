package exchange

import (
	"context"
	"time"

	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

// Trade 逐笔成交。对应 C# Trade。
type Trade struct {
	Timestamp    time.Time
	Price        decimal.Decimal
	Quantity     decimal.Decimal
	IsBuyerMaker bool
}

// MarketDataStreamClient 实时行情订阅（公开端点，无需认证）。
// 回调式：每条消息回调一次，直到 ctx 取消或连接出错返回；调用方负责重连。
type MarketDataStreamClient interface {
	SubscribeTrades(ctx context.Context, pair string, onTrade func(Trade)) error
	SubscribeKlines(ctx context.Context, pair, interval string, onKline func(domain.Kline)) error
}

// ExchangeOrderDTO 是交易所订单的统一表示，对应 C# ExchangeOrderDto。
type ExchangeOrderDTO struct {
	Pair            string
	Side            string
	Type            string
	Status          string
	Price           decimal.Decimal
	Quantity        decimal.Decimal
	FilledQuantity  decimal.Decimal
	ExchangeOrderID string
	PlacedAt        time.Time
}

// AccountClient 账户/持仓查询（读端点，需认证）。对应 C# IAccountClient 的 Worker 所需子集。
type AccountClient interface {
	// GetAssetBalances 返回所有余额非零的资产 → 总额（free+locked）。
	GetAssetBalances(ctx context.Context) (map[string]decimal.Decimal, error)
	// GetOrderHistoryByPair 返回指定交易对最多 limit 条历史订单。
	GetOrderHistoryByPair(ctx context.Context, pair string, limit int) ([]ExchangeOrderDTO, error)
	// GetOpenOrders 返回所有未结订单（孤儿检测用）。
	GetOpenOrders(ctx context.Context) ([]ExchangeOrderDTO, error)
}

// OrderResult 订单查询/操作结果，对应 C# OrderResult。
// Success=false 表示交易所明确返回失败（如订单不存在 / 不支持），非传输异常；
// 传输异常通过返回的 error 表达。
type OrderResult struct {
	Success         bool
	ExchangeOrderID string
	FilledQuantity  decimal.Decimal
	AvgPrice        decimal.Decimal
	Fee             decimal.Decimal
	Error           string
	FeeAsset        string
}

// OrderQueryClient 订单状态查询（对账用）。对应 C# ITradingClient 的查询子集。
type OrderQueryClient interface {
	GetOrder(ctx context.Context, pair, exchangeOrderID string) (OrderResult, error)
	// GetOrderByClientOrderID 凭 ClientOrderId 反查；不支持的客户端返回 Success=false, Error="not_supported"。
	GetOrderByClientOrderID(ctx context.Context, pair, clientOrderID string) (OrderResult, error)
}

// OrderBookLevel 订单簿一档（价/量）。
type OrderBookLevel struct {
	Price    decimal.Decimal
	Quantity decimal.Decimal
}

// OrderBook 订单簿快照。Bids 买盘从高到低，Asks 卖盘从低到高。
type OrderBook struct {
	Bids []OrderBookLevel
	Asks []OrderBookLevel
}

// PairRule 交易对规则。对应 C# PairRule 的下单所需子集。
type PairRule struct {
	Pair        string
	MinNotional decimal.Decimal
	MinQuantity decimal.Decimal
	StepSize    decimal.Decimal
}

// OrderRequest 下单请求。对应 C# OrderRequest。
type OrderRequest struct {
	Pair          string
	Side          domain.OrderSide
	Type          domain.OrderType
	Quantity      decimal.Decimal
	Price         *decimal.Decimal
	ClientOrderID string
}

// TradingClient 下单 + 规则/订单簿查询（写端点 + 下单所需读端点）。对应 C# ITradingClient 子集。
type TradingClient interface {
	PlaceOrder(ctx context.Context, req OrderRequest) (OrderResult, error)
	GetOrderBook(ctx context.Context, pair string, limit int) (OrderBook, error)
	GetPairRules(ctx context.Context) ([]PairRule, error)
}

// Client 是交易所客户端的聚合能力接口，随移植阶段逐步扩展
// （Stage 2: 账户查询；Stage 3: 订单查询；Stage 4: 行情订阅/下单）。
type Client interface {
	AccountClient
	OrderQueryClient
	TradingClient
	Type() domain.ExchangeType
}
