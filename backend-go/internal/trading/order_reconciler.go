package trading

import (
	"context"
	"fmt"
	"strings"
	"time"

	"github.com/rs/zerolog"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

// OrderReconciler 订单对账器。周期扫描 Pending 订单，向交易所核实状态实现崩溃恢复。
// 对应 C# OrderReconciler。
type OrderReconciler struct {
	exchangeRepo domain.ExchangeRepository
	orderRepo    domain.OrderRepository
	provider     *ExchangeClientProvider
	eventBus     DomainEventBus
	fillProj     *FillProjector
	settings     RiskSettings
	log          zerolog.Logger
}

// NewOrderReconciler 构造订单对账器。
func NewOrderReconciler(
	exchangeRepo domain.ExchangeRepository,
	orderRepo domain.OrderRepository,
	provider *ExchangeClientProvider,
	eventBus DomainEventBus,
	fillProj *FillProjector,
	settings RiskSettings,
	log zerolog.Logger,
) *OrderReconciler {
	return &OrderReconciler{
		exchangeRepo: exchangeRepo, orderRepo: orderRepo, provider: provider,
		eventBus: eventBus, fillProj: fillProj, settings: settings, log: log,
	}
}

// Reconcile 执行一轮订单对账。
func (r *OrderReconciler) Reconcile(ctx context.Context) error {
	staleness := time.Duration(max(1, r.settings.StalePendingMinutes)) * time.Minute

	exchanges, err := r.exchangeRepo.GetAllEnabled(ctx)
	if err != nil {
		return err
	}
	if len(exchanges) == 0 {
		r.log.Debug().Msg("Reconciliation: 无已启用交易所")
		return nil
	}

	totalChecked, totalFixed := 0, 0

	for _, ex := range exchanges {
		if ctx.Err() != nil {
			break
		}
		pending, err := r.orderRepo.GetPendingByExchange(ctx, ex.ID)
		if err != nil {
			r.log.Error().Err(err).Str("exchange_id", ex.ID.String()).Msg("对账：查询 Pending 订单失败")
			continue
		}
		if len(pending) == 0 {
			continue
		}
		totalChecked += len(pending)

		// 客户端按交易所惰性创建并缓存一次。
		var client exchange.Client
		clientInit := false
		getClient := func() exchange.Client {
			if !clientInit {
				client = r.provider.TryCreate(ex)
				clientInit = true
			}
			return client
		}

		for _, order := range pending {
			if ctx.Err() != nil {
				break
			}
			if order.Status != domain.OrderStatusPending {
				continue
			}
			age := time.Since(order.PlacedAtUtc)

			if order.ExchangeOrderID == "" {
				if r.reconcileWithoutExchangeID(ctx, ex, order, age, staleness, getClient) {
					totalFixed++
				}
				continue
			}

			if r.reconcileWithExchangeID(ctx, order, age, staleness, getClient) {
				totalFixed++
			}
		}
	}

	if totalChecked > 0 {
		r.log.Info().Int("checked", totalChecked).Int("fixed", totalFixed).Msg("Reconciliation 完成")
	}
	return nil
}

// reconcileWithoutExchangeID 处理无 ExchangeOrderId 的订单（凭 ClientOrderId 反查 / 超时判定）。返回是否计入 fixed。
func (r *OrderReconciler) reconcileWithoutExchangeID(
	ctx context.Context, ex *domain.Exchange, order *domain.Order,
	age, staleness time.Duration, getClient func() exchange.Client,
) bool {
	if client := getClient(); client != nil {
		lookup, err := client.GetOrderByClientOrderID(ctx, order.Pair, clientOrderIDHex(order))
		if err == nil {
			if lookup.Success {
				order.ExchangeOrderID = lookup.ExchangeOrderID
				if _, err := r.applyResultToOrder(order, lookup); err != nil {
					r.log.Error().Err(err).Str("order_id", order.ID.String()).Msg("对账应用成交结果失败")
				}
				if err := r.orderRepo.Update(ctx, order); err != nil {
					r.log.Error().Err(err).Str("order_id", order.ID.String()).Msg("对账更新订单失败")
					return false
				}
				r.maybeProjectFill(ctx, order, lookup)
				r.log.Info().Str("order_id", order.ID.String()).Str("exchange_order_id", order.ExchangeOrderID).
					Str("status", string(order.Status)).Msg("Reconciliation 凭 ClientOrderId 恢复订单")
				return true
			}
			if age >= staleness {
				return r.markFailed(ctx, order, fmt.Sprintf("对账：交易所无此 ClientOrderId (%s)", lookup.Error))
			}
			return false
		}
		r.log.Warn().Err(err).Str("order_id", order.ID.String()).Msg("Reconciliation 按 ClientOrderId 反查异常")
	}

	if age >= staleness {
		return r.markFailed(ctx, order, "对账超时（无 ExchangeOrderId 且无法反查）")
	}
	return false
}

// reconcileWithExchangeID 处理有 ExchangeOrderId 的订单（向交易所核实状态）。返回是否计入 fixed。
func (r *OrderReconciler) reconcileWithExchangeID(
	ctx context.Context, order *domain.Order,
	age, staleness time.Duration, getClient func() exchange.Client,
) bool {
	client := getClient()
	if client == nil {
		if age >= staleness*2 {
			return r.markFailed(ctx, order, "交易所客户端不可用，长时间无法核实")
		}
		return false
	}

	result, err := client.GetOrder(ctx, order.Pair, order.ExchangeOrderID)
	if err != nil {
		r.log.Warn().Err(err).Str("order_id", order.ID.String()).Str("exchange_order_id", order.ExchangeOrderID).
			Msg("Reconciliation 查询交易所异常")
		return false
	}
	if result.Success {
		changed, applyErr := r.applyResultToOrder(order, result)
		if applyErr != nil {
			return false
		}
		if changed {
			if err := r.orderRepo.Update(ctx, order); err != nil {
				r.log.Error().Err(err).Str("order_id", order.ID.String()).Msg("对账更新订单失败")
				return false
			}
			r.maybeProjectFill(ctx, order, result)
			r.log.Info().Str("order_id", order.ID.String()).Str("status", string(order.Status)).
				Msg("Reconciliation 修复订单")
			return true
		}
		return false
	}
	if age >= staleness {
		return r.markFailed(ctx, order, fmt.Sprintf("交易所查询失败: %s", result.Error))
	}
	return false
}

// DetectOrphanOrders 扫描"交易所有/本地无"的孤儿订单，发事件告警，返回数量。
func (r *OrderReconciler) DetectOrphanOrders(ctx context.Context) (int, error) {
	exchanges, err := r.exchangeRepo.GetAllEnabled(ctx)
	if err != nil {
		return 0, err
	}
	if len(exchanges) == 0 {
		return 0, nil
	}

	total := 0
	for _, ex := range exchanges {
		if ctx.Err() != nil {
			break
		}
		client := r.provider.TryCreate(ex)
		if client == nil {
			continue
		}
		openOrders, err := client.GetOpenOrders(ctx)
		if err != nil {
			r.log.Warn().Err(err).Str("exchange_id", ex.ID.String()).Msg("孤儿检测: 拉取未结订单失败")
			continue
		}
		for _, remote := range openOrders {
			if ctx.Err() != nil {
				break
			}
			if remote.ExchangeOrderID == "" {
				continue
			}
			local, err := r.orderRepo.GetByExchangeOrderID(ctx, remote.ExchangeOrderID)
			if err != nil {
				r.log.Warn().Err(err).Msg("孤儿检测: 查询本地订单失败")
				continue
			}
			if local != nil {
				continue
			}
			total++
			if err := r.eventBus.Publish(ctx, OrphanOrderDetectedPayload{
				ExchangeID:      ex.ID,
				ExchangeType:    string(ex.Type),
				Pair:            remote.Pair,
				ExchangeOrderID: remote.ExchangeOrderID,
				Side:            remote.Side,
				Type:            remote.Type,
				Price:           remote.Price,
				Quantity:        remote.Quantity,
				DetectedAt:      time.Now().UTC(),
			}); err != nil {
				r.log.Error().Err(err).Msg("发布孤儿订单事件失败")
			}
			r.log.Warn().Str("exchange_id", ex.ID.String()).Str("pair", remote.Pair).
				Str("exchange_order_id", remote.ExchangeOrderID).Msg("孤儿订单检测")
		}
	}
	if total > 0 {
		r.log.Warn().Int("count", total).Msg("孤儿订单巡检完成")
	}
	return total, nil
}

// maybeProjectFill 对账中订单转 Filled 时触发幂等的"成交→持仓"投影。
func (r *OrderReconciler) maybeProjectFill(ctx context.Context, order *domain.Order, result exchange.OrderResult) {
	if order.Status != domain.OrderStatusFilled {
		return
	}
	if err := r.fillProj.ProjectFilled(ctx, order, result.AvgPrice); err != nil {
		r.log.Error().Err(err).Str("order_id", order.ID.String()).Msg("对账：成交→持仓投影失败")
	}
}

// applyResultToOrder 把交易所结果应用到订单，返回状态是否变化。
func (r *OrderReconciler) applyResultToOrder(order *domain.Order, result exchange.OrderResult) (bool, error) {
	if order.IsTerminal() {
		return false, nil
	}
	prev := order.Status
	var feeAsset *string
	if result.FeeAsset != "" {
		feeAsset = &result.FeeAsset
	}
	if err := order.RecordFill(result.FilledQuantity, result.Fee, nil, feeAsset); err != nil {
		return false, err
	}
	return order.Status != prev, nil
}

func (r *OrderReconciler) markFailed(ctx context.Context, order *domain.Order, reason string) bool {
	if order.IsTerminal() {
		return false
	}
	if err := order.MarkFailed(reason); err != nil {
		return false
	}
	if err := r.orderRepo.Update(ctx, order); err != nil {
		r.log.Error().Err(err).Str("order_id", order.ID.String()).Msg("对账标记失败时更新失败")
		return false
	}
	r.log.Warn().Str("order_id", order.ID.String()).Str("reason", reason).Msg("Reconciliation 标记订单失败")
	return true
}

// clientOrderIDHex 返回 ClientOrderId 的无连字符 32 位十六进制（对应 C# Guid.ToString("N")）。
func clientOrderIDHex(order *domain.Order) string {
	return strings.ReplaceAll(order.ClientOrderID.String(), "-", "")
}
