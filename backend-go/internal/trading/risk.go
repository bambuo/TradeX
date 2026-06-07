package trading

import (
	"context"
	"fmt"
	"sync"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

// RiskResult 风控结果。对应 C# RiskResult。
type RiskResult struct {
	IsAllowed     bool
	DeniedReasons []string
}

// riskContext 风控上下文，对应 C# RiskContext（持有评估所需快照与阈值）。
type riskContext struct {
	TraderID             uuid.UUID
	ExchangeID           uuid.UUID
	Pair                 string
	PortfolioValue       decimal.Decimal
	DailyLoss            decimal.Decimal
	DailyProfit          decimal.Decimal
	ConsecutiveLossCount int
	OpenPositionCount    int
	LastTradeTimeUtc     *time.Time
	OrderNotional        *decimal.Decimal

	settings RiskSettings
	denied   []string
}

func (c *riskContext) deny(reason string) { c.denied = append(c.denied, reason) }

// Pinger 交易所连通性检测（*exchange.Factory 即满足）。
type Pinger interface {
	Ping(ctx context.Context, t domain.ExchangeType) error
}

// PortfolioRiskManager 组合级 + 币种级风控。对应 C# PortfolioRiskManager。
type PortfolioRiskManager struct {
	positionRepo domain.PositionRepository
	exchangeRepo domain.ExchangeRepository
	killSwitch   *KillSwitch
	pinger       Pinger
	settings     RiskSettings
	log          zerolog.Logger

	healthMu    sync.Mutex
	healthCache map[uuid.UUID]healthEntry
}

type healthEntry struct {
	healthy   bool
	checkedAt time.Time
}

// NewPortfolioRiskManager 构造风控管理器。
func NewPortfolioRiskManager(
	positionRepo domain.PositionRepository,
	exchangeRepo domain.ExchangeRepository,
	killSwitch *KillSwitch,
	pinger Pinger,
	settings RiskSettings,
	log zerolog.Logger,
) *PortfolioRiskManager {
	return &PortfolioRiskManager{
		positionRepo: positionRepo, exchangeRepo: exchangeRepo, killSwitch: killSwitch,
		pinger: pinger, settings: settings, log: log, healthCache: map[uuid.UUID]healthEntry{},
	}
}

// Check 组合级风控（无交易对/名义价值）。对应 C# CheckAsync。
func (m *PortfolioRiskManager) Check(ctx context.Context, traderID, exchangeID uuid.UUID) (RiskResult, error) {
	return m.run(ctx, traderID, exchangeID, "", nil)
}

// CheckPairRisk 币种级风控（带交易对与计划名义价值）。对应 C# CheckPairRiskAsync。
func (m *PortfolioRiskManager) CheckPairRisk(ctx context.Context, traderID, exchangeID uuid.UUID, pair string, orderNotional *decimal.Decimal) (RiskResult, error) {
	return m.run(ctx, traderID, exchangeID, pair, orderNotional)
}

func (m *PortfolioRiskManager) run(ctx context.Context, traderID, exchangeID uuid.UUID, pair string, orderNotional *decimal.Decimal) (RiskResult, error) {
	c, err := m.buildContext(ctx, traderID, exchangeID, pair, orderNotional)
	if err != nil {
		return RiskResult{}, err
	}
	// 责任链顺序与 C# BuildChain 一致。
	m.checkDailyLoss(c)
	m.checkDrawdown(c)
	m.checkConsecutiveLoss(c)
	m.checkCircuitBreaker(c)
	m.checkCooldown(c)
	m.checkPositionLimit(c)
	m.checkMaxOrderNotional(c)
	m.checkSlippage(c)
	m.checkExchangeHealth(ctx, c)
	return RiskResult{IsAllowed: len(c.denied) == 0, DeniedReasons: c.denied}, nil
}

func (m *PortfolioRiskManager) buildContext(ctx context.Context, traderID, exchangeID uuid.UUID, pair string, orderNotional *decimal.Decimal) (*riskContext, error) {
	openPositions, err := m.positionRepo.GetOpenByTraderID(ctx, traderID)
	if err != nil {
		return nil, err
	}
	todayStart := time.Now().UTC().Truncate(24 * time.Hour)
	closedToday, err := m.positionRepo.GetClosedByTraderIDSince(ctx, traderID, todayStart)
	if err != nil {
		return nil, err
	}

	dailyRealized := decimal.Zero
	for _, p := range closedToday {
		dailyRealized = dailyRealized.Add(p.RealizedPnl)
	}
	dailyLoss := decimal.Min(dailyRealized, decimal.Zero).Abs()
	dailyProfit := decimal.Max(dailyRealized, decimal.Zero)

	// 连续亏损：取最近至多 MaxConsecutiveLosses 笔，从最新起连续为负的数量。
	consecutive := 0
	limit := m.settings.MaxConsecutiveLosses
	for i, p := range closedToday {
		if i >= limit {
			break
		}
		if p.RealizedPnl.IsNegative() {
			consecutive++
		} else {
			break
		}
	}

	portfolioValue := decimal.Zero
	var lastTrade *time.Time
	for _, p := range openPositions {
		portfolioValue = portfolioValue.Add(p.CurrentPrice.Mul(p.Quantity))
		if lastTrade == nil || p.OpenedAtUtc.After(*lastTrade) {
			t := p.OpenedAtUtc
			lastTrade = &t
		}
	}
	for _, p := range closedToday {
		if p.ClosedAtUtc != nil && (lastTrade == nil || p.ClosedAtUtc.After(*lastTrade)) {
			lastTrade = p.ClosedAtUtc
		}
	}

	return &riskContext{
		TraderID: traderID, ExchangeID: exchangeID, Pair: pair,
		PortfolioValue: portfolioValue, DailyLoss: dailyLoss, DailyProfit: dailyProfit,
		ConsecutiveLossCount: consecutive, OpenPositionCount: len(openPositions),
		LastTradeTimeUtc: lastTrade, OrderNotional: orderNotional, settings: m.settings,
	}, nil
}

func (m *PortfolioRiskManager) checkDailyLoss(c *riskContext) {
	if c.DailyLoss.GreaterThan(c.settings.MaxDailyLoss) {
		c.deny(fmt.Sprintf("当日亏损 %s 超过限制 %s", c.DailyLoss, c.settings.MaxDailyLoss))
	}
}

func (m *PortfolioRiskManager) checkDrawdown(c *riskContext) {
	if c.PortfolioValue.IsPositive() {
		drawdown := c.DailyLoss.Abs().Div(c.PortfolioValue).Mul(decimal.NewFromInt(100))
		if drawdown.GreaterThan(c.settings.MaxDrawdownPercent) {
			c.deny(fmt.Sprintf("回撤 %s%% 超过限制 %s%%", drawdown.StringFixed(2), c.settings.MaxDrawdownPercent))
		}
	}
}

func (m *PortfolioRiskManager) checkConsecutiveLoss(c *riskContext) {
	if c.ConsecutiveLossCount >= c.settings.MaxConsecutiveLosses {
		c.deny(fmt.Sprintf("连续亏损 %d 次超过限制 %d", c.ConsecutiveLossCount, c.settings.MaxConsecutiveLosses))
	}
}

func (m *PortfolioRiskManager) checkCircuitBreaker(c *riskContext) {
	if m.killSwitch != nil && m.killSwitch.IsActive() {
		reason := m.killSwitch.LastReason()
		if reason == "" {
			reason = "无原因"
		}
		c.deny("Kill Switch 已激活: " + reason)
	} else if c.settings.CircuitBreakerActive {
		c.deny("熔断机制已激活，暂停所有交易")
	}
}

func (m *PortfolioRiskManager) checkCooldown(c *riskContext) {
	if c.LastTradeTimeUtc != nil && c.settings.CooldownSeconds > 0 {
		elapsed := time.Since(*c.LastTradeTimeUtc)
		if elapsed.Seconds() < float64(c.settings.CooldownSeconds) {
			c.deny(fmt.Sprintf("冷却期未结束: 距上次交易 %.0fs, 需要 %ds", elapsed.Seconds(), c.settings.CooldownSeconds))
		}
	}
}

func (m *PortfolioRiskManager) checkPositionLimit(c *riskContext) {
	if c.OpenPositionCount >= c.settings.MaxOpenPositions {
		c.deny(fmt.Sprintf("持仓数量 %d 超过限制 %d", c.OpenPositionCount, c.settings.MaxOpenPositions))
	}
}

func (m *PortfolioRiskManager) checkMaxOrderNotional(c *riskContext) {
	if c.settings.MaxOrderNotional.IsPositive() && c.OrderNotional != nil && c.OrderNotional.GreaterThan(c.settings.MaxOrderNotional) {
		c.deny(fmt.Sprintf("单笔名义价值 %s 超过限制 %s", c.OrderNotional.StringFixed(2), c.settings.MaxOrderNotional.StringFixed(2)))
	}
}

func (m *PortfolioRiskManager) checkSlippage(c *riskContext) {
	if c.OrderNotional == nil {
		return
	}
	slippage := c.OrderNotional.Mul(c.settings.SlippageTolerance).Abs()
	if slippage.GreaterThan(c.settings.MaxSlippageAmount) {
		c.deny(fmt.Sprintf("滑点预估 %s 超过限制 %s", slippage.StringFixed(2), c.settings.MaxSlippageAmount))
	}
}

const healthCacheTTL = 30 * time.Second

func (m *PortfolioRiskManager) checkExchangeHealth(ctx context.Context, c *riskContext) {
	if m.pinger == nil {
		return
	}
	m.healthMu.Lock()
	cached, ok := m.healthCache[c.ExchangeID]
	m.healthMu.Unlock()
	if ok && time.Since(cached.checkedAt) < healthCacheTTL {
		if !cached.healthy {
			c.deny("交易所健康检查失败 (缓存)")
		}
		return
	}

	ex, err := m.exchangeRepo.GetByID(ctx, c.ExchangeID)
	if err != nil || ex == nil {
		m.setHealth(c.ExchangeID, false)
		c.deny("交易所不存在")
		return
	}
	healthy := m.pinger.Ping(ctx, ex.Type) == nil
	m.setHealth(c.ExchangeID, healthy)
	if !healthy {
		c.deny("交易所连接失败")
	}
}

func (m *PortfolioRiskManager) setHealth(id uuid.UUID, healthy bool) {
	m.healthMu.Lock()
	m.healthCache[id] = healthEntry{healthy: healthy, checkedAt: time.Now()}
	m.healthMu.Unlock()
}
