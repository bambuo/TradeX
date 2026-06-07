// Package streaming 移植自 C# TradeX.Trading.Streaming：
// 逐笔成交（Trade）与 K 线收盘（Kline）WebSocket 订阅管理，推送到有界事件通道。
package streaming

import (
	"context"

	"github.com/google/uuid"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

// TradeEvent 一条逐笔成交事件（含来源交易所信息）。
type TradeEvent struct {
	Pair         string
	ExchangeType domain.ExchangeType
	ExchangeID   uuid.UUID
	Trade        exchange.Trade
}

// KlineEvent 一根已收盘 K 线事件。
type KlineEvent struct {
	Pair         string
	ExchangeType domain.ExchangeType
	ExchangeID   uuid.UUID
	Interval     string
	Kline        domain.Kline
}

// PublicClientFactory 创建无认证行情订阅客户端（*exchange.Factory 即满足）。
type PublicClientFactory interface {
	CreatePublicClient(t domain.ExchangeType) (exchange.MarketDataStreamClient, error)
}

// dropOldestSend 非阻塞写入：通道满时丢弃最旧的一条再写，复刻 C#
// Channel.CreateBounded(FullMode = DropOldest) 语义。
func dropOldestSend[T any](ch chan T, v T) {
	for {
		select {
		case ch <- v:
			return
		default:
			select {
			case <-ch: // 丢最旧
			default:
			}
		}
	}
}

// resolveExchangeType 解析交易所实际类型，失败回退 Binance。
func resolveExchangeType(ctx context.Context, repo domain.ExchangeRepository, id uuid.UUID) domain.ExchangeType {
	ex, err := repo.GetByID(ctx, id)
	if err != nil || ex == nil {
		return domain.ExchangeTypeBinance
	}
	return ex.Type
}
