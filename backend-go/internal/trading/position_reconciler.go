package trading

import (
	"context"
	"sort"
	"strings"
	"time"

	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"

	"tradex/internal/domain"
)

var hundred = decimal.NewFromInt(100)

// PositionReconciler 持仓级对账器：用交易所余额校验本地开仓量，漂移超阈值则告警。
// 仅检测不自动修改。对应 C# PositionReconciler。
type PositionReconciler struct {
	exchangeRepo domain.ExchangeRepository
	positionRepo domain.PositionRepository
	provider     *ExchangeClientProvider
	eventBus     DomainEventBus
	metrics      *Metrics
	settings     RiskSettings
	log          zerolog.Logger
}

// NewPositionReconciler 构造持仓对账器。
func NewPositionReconciler(
	exchangeRepo domain.ExchangeRepository,
	positionRepo domain.PositionRepository,
	provider *ExchangeClientProvider,
	eventBus DomainEventBus,
	metrics *Metrics,
	settings RiskSettings,
	log zerolog.Logger,
) *PositionReconciler {
	return &PositionReconciler{
		exchangeRepo: exchangeRepo, positionRepo: positionRepo, provider: provider,
		eventBus: eventBus, metrics: metrics, settings: settings, log: log,
	}
}

// ReconcilePositions 执行一轮持仓对账，返回漂移项数。
func (r *PositionReconciler) ReconcilePositions(ctx context.Context) (int, error) {
	if !r.settings.PositionReconcileEnabled {
		return 0, nil
	}
	exchanges, err := r.exchangeRepo.GetAllEnabled(ctx)
	if err != nil {
		return 0, err
	}
	if len(exchanges) == 0 {
		return 0, nil
	}

	// base 资产后缀按长度降序匹配，避免 "USD" 抢先于 "USDT"。
	quotes := make([]string, 0, len(r.settings.QuoteAssets))
	for _, q := range r.settings.QuoteAssets {
		if strings.TrimSpace(q) != "" {
			quotes = append(quotes, q)
		}
	}
	sort.SliceStable(quotes, func(i, j int) bool { return len(quotes[i]) > len(quotes[j]) })

	allOpen, err := r.positionRepo.GetAllOpen(ctx)
	if err != nil {
		return 0, err
	}
	tolerance := r.settings.PositionDriftTolerancePercent
	minAbs := r.settings.PositionDriftMinAbsolute
	totalDrift := 0

	for _, ex := range exchanges {
		if ctx.Err() != nil {
			break
		}

		// 本地：按 base 资产聚合该交易所的开仓量。
		localByAsset := map[string]decimal.Decimal{}
		for _, p := range allOpen {
			if p.ExchangeID != ex.ID {
				continue
			}
			asset := ResolveBaseAsset(p.Pair, quotes)
			if asset == "" {
				r.log.Debug().Str("pair", p.Pair).Msg("持仓对账：无法解析 base 资产，跳过")
				continue
			}
			localByAsset[asset] = localByAsset[asset].Add(p.Quantity)
		}
		if len(localByAsset) == 0 {
			continue
		}

		client := r.provider.TryCreate(ex)
		if client == nil {
			continue
		}
		balances, err := client.GetAssetBalances(ctx)
		if err != nil {
			r.log.Warn().Err(err).Str("exchange_id", ex.ID.String()).Msg("持仓对账：拉取交易所余额失败")
			continue
		}

		for asset, localQty := range localByAsset {
			if ctx.Err() != nil {
				break
			}
			actualQty := balances[asset] // 缺省零值
			drift := localQty.Sub(actualQty)
			absDrift := drift.Abs()
			if absDrift.LessThanOrEqual(minAbs) {
				continue
			}
			basis := decimal.Max(localQty, actualQty)
			if !basis.IsPositive() {
				continue
			}
			driftPct := absDrift.Div(basis).Mul(hundred)
			if driftPct.LessThan(tolerance) {
				continue
			}
			// 盈余方向（实际 > 本地）默认不上报。
			if drift.IsNegative() && !r.settings.PositionDriftReportSurplus {
				continue
			}

			severity := "Warning"
			if drift.IsPositive() {
				severity = "Critical"
			}
			totalDrift++

			if err := r.eventBus.Publish(ctx, PositionDriftDetectedPayload{
				ExchangeID:       ex.ID,
				ExchangeType:     string(ex.Type),
				TraderID:         nil,
				Asset:            asset,
				LocalQuantity:    localQty,
				ExchangeQuantity: actualQty,
				Drift:            drift,
				DriftPercent:     driftPct.Round(4),
				Severity:         severity,
				DetectedAt:       time.Now().UTC(),
			}); err != nil {
				r.log.Error().Err(err).Msg("发布持仓漂移事件失败")
			}

			r.metrics.PositionDriftDetected.Add(ctx, 1, metric.WithAttributes(
				attribute.String("exchange", string(ex.Type)),
				attribute.String("severity", severity),
			))

			evt := r.log.Warn()
			if drift.IsPositive() {
				evt = r.log.Error()
			}
			evt.Str("exchange_id", ex.ID.String()).Str("asset", asset).
				Str("local", localQty.String()).Str("actual", actualQty.String()).
				Str("drift", drift.String()).Str("pct", driftPct.StringFixed(2)).
				Str("severity", severity).Msg("持仓漂移告警")
		}
	}

	if totalDrift > 0 {
		r.log.Warn().Int("count", totalDrift).Msg("持仓对账完成")
	}
	return totalDrift, nil
}

// ResolveBaseAsset 从交易对名切出 base 资产：归一化分隔符后按 quote 后缀（长度降序）剥离。
// 无法识别返回 ""。对应 C# PositionReconciler.ResolveBaseAsset。
func ResolveBaseAsset(pair string, quotesLongestFirst []string) string {
	if strings.TrimSpace(pair) == "" {
		return ""
	}
	normalized := strings.ToUpper(strings.TrimSpace(
		strings.NewReplacer("_", "", "-", "", "/", "").Replace(pair)))
	for _, quote := range quotesLongestFirst {
		q := strings.ToUpper(quote)
		if len(normalized) > len(q) && strings.HasSuffix(normalized, q) {
			return normalized[:len(normalized)-len(q)]
		}
	}
	return ""
}
