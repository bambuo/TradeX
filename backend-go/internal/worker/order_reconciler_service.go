package worker

import (
	"context"
	"time"

	"github.com/rs/zerolog"

	"tradex/internal/trading"
)

// OrderReconcilerService 周期性触发订单对账 + 持仓对账的后台服务。
// 对应 C# TradeX.Trading.Execution.OrderReconcilerService。
type OrderReconcilerService struct {
	orderRec    *trading.OrderReconciler
	positionRec *trading.PositionReconciler
	settings    trading.RiskSettings
	log         zerolog.Logger
}

// NewOrderReconcilerService 构造对账服务。
func NewOrderReconcilerService(
	orderRec *trading.OrderReconciler,
	positionRec *trading.PositionReconciler,
	settings trading.RiskSettings,
	log zerolog.Logger,
) *OrderReconcilerService {
	return &OrderReconcilerService{
		orderRec: orderRec, positionRec: positionRec, settings: settings,
		log: log.With().Str("service", "OrderReconciler").Logger(),
	}
}

func (s *OrderReconcilerService) Name() string { return "OrderReconciler" }

func (s *OrderReconcilerService) Run(ctx context.Context) error {
	interval := time.Duration(max(10, s.settings.OrderReconcileIntervalSeconds)) * time.Second
	s.log.Info().Dur("interval", interval).Msg("OrderReconcilerService 启动")

	// 启动时一次性扫描孤儿订单（交易所有/本地无）。
	if orphans, err := s.orderRec.DetectOrphanOrders(ctx); err != nil {
		if ctx.Err() != nil {
			return nil
		}
		s.log.Error().Err(err).Msg("启动孤儿订单扫描异常, 跳过")
	} else if orphans > 0 {
		s.log.Warn().Int("count", orphans).Msg("启动孤儿订单扫描: 已写入事件总线告警")
	}

	positionInterval := time.Duration(max(30, s.settings.PositionReconcileIntervalSeconds)) * time.Second
	var lastPositionRun time.Time // 零值 → 首轮即触发

	// 启动后先等一个周期，避开与首轮订单写入的竞争窗口。
	if err := sleep(ctx, interval); err != nil {
		return nil
	}

	for ctx.Err() == nil {
		if err := s.orderRec.Reconcile(ctx); err != nil && ctx.Err() == nil {
			s.log.Error().Err(err).Msg("OrderReconcilerService 周期执行异常，将于下一周期重试")
		}

		if time.Since(lastPositionRun) >= positionInterval {
			if _, err := s.positionRec.ReconcilePositions(ctx); err != nil && ctx.Err() == nil {
				s.log.Error().Err(err).Msg("持仓对账异常，将于下一周期重试")
			}
			lastPositionRun = time.Now()
		}

		if err := sleep(ctx, interval); err != nil {
			break
		}
	}

	s.log.Info().Msg("OrderReconcilerService 已停止")
	return nil
}
