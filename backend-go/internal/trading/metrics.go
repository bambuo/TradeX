package trading

import (
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/metric"
)

// MeterName 与 C# TradeXMetrics.MeterName 一致，Prometheus 抓取前缀 tradex_*。
const MeterName = "tradex"

// Metrics 自定义业务指标。对应 C# TradeXMetrics 中实盘 Worker 用到的子集。
type Metrics struct {
	OrdersPlaced          metric.Int64Counter
	OrdersRejected        metric.Int64Counter
	RiskDenials           metric.Int64Counter
	PositionDriftDetected metric.Int64Counter
}

// NewMetrics 用全局 MeterProvider 创建指标（需先 telemetry.InitOTel）。
func NewMetrics() (*Metrics, error) {
	m := otel.Meter(MeterName)

	ordersPlaced, err := m.Int64Counter("tradex.orders.placed",
		metric.WithUnit("{order}"),
		metric.WithDescription("成功提交至交易所的订单数（按交易所/方向/状态打标签）"))
	if err != nil {
		return nil, err
	}
	ordersRejected, err := m.Int64Counter("tradex.orders.rejected",
		metric.WithUnit("{order}"),
		metric.WithDescription("下单失败计数（按 reason 打标签）"))
	if err != nil {
		return nil, err
	}
	riskDenials, err := m.Int64Counter("tradex.risk.denials",
		metric.WithUnit("{denial}"),
		metric.WithDescription("风控拒绝计数（按 scope 打标签）"))
	if err != nil {
		return nil, err
	}
	posDrift, err := m.Int64Counter("tradex.position.drift_detected",
		metric.WithUnit("{event}"),
		metric.WithDescription("持仓级对账发现漂移超阈值计数（按 exchange/severity 打标签）"))
	if err != nil {
		return nil, err
	}

	return &Metrics{
		OrdersPlaced:          ordersPlaced,
		OrdersRejected:        ordersRejected,
		RiskDenials:           riskDenials,
		PositionDriftDetected: posDrift,
	}, nil
}
