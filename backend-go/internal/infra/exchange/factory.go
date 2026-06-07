package exchange

import (
	"context"
	"fmt"

	"tradex/internal/domain"
)

// Factory 按交易所类型构造带认证的客户端，对应 C# ExchangeClientFactory。
type Factory struct{}

// NewFactory 构造一个工厂。
func NewFactory() *Factory { return &Factory{} }

// CreateClient 创建指定交易所的客户端。
// 目前仅 Binance 已移植；其余交易所返回明确错误，由调用方按交易所隔离处理。
func (f *Factory) CreateClient(t domain.ExchangeType, apiKey, secretKey string, passphrase *string) (Client, error) {
	switch t {
	case domain.ExchangeTypeBinance:
		return NewBinanceAdapter(apiKey, secretKey, false), nil
	case domain.ExchangeTypeOKX, domain.ExchangeTypeGate, domain.ExchangeTypeBybit, domain.ExchangeTypeHTX:
		return nil, fmt.Errorf("交易所 %s 适配尚未移植", t)
	default:
		return nil, fmt.Errorf("不支持的交易所类型: %s", t)
	}
}

// Ping 检测指定交易所的连通性（公开端点，无需认证）。供风控健康检查使用。
func (f *Factory) Ping(ctx context.Context, t domain.ExchangeType) error {
	switch t {
	case domain.ExchangeTypeBinance:
		return NewBinanceAdapter("", "", false).Ping(ctx)
	default:
		return fmt.Errorf("交易所 %s 健康检查尚未移植", t)
	}
}

// CreatePublicClient 创建无认证的行情订阅客户端（公开 WS 端点）。
func (f *Factory) CreatePublicClient(t domain.ExchangeType) (MarketDataStreamClient, error) {
	switch t {
	case domain.ExchangeTypeBinance:
		return NewBinanceAdapter("", "", false), nil
	case domain.ExchangeTypeOKX, domain.ExchangeTypeGate, domain.ExchangeTypeBybit, domain.ExchangeTypeHTX:
		return nil, fmt.Errorf("交易所 %s 行情订阅尚未移植", t)
	default:
		return nil, fmt.Errorf("不支持的交易所类型: %s", t)
	}
}
