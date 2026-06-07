package trading

import (
	"context"
	"fmt"
	"strings"
	"sync"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"
	"golang.org/x/sync/errgroup"

	"tradex/internal/domain"
	"tradex/internal/trading/streaming"
)

const maxPriceHistory = 2000

type KlineWindow struct {
	Prices []decimal.Decimal
	Open   decimal.Decimal
	High   decimal.Decimal
	Low    decimal.Decimal
	Close  decimal.Decimal
}

type priceHistory struct {
	mu     sync.Mutex
	prices []decimal.Decimal
}

type StrategyEvaluator struct {
	bindingRepo  domain.StrategyBindingRepository
	strategyRepo domain.StrategyRepository
	positionRepo domain.PositionRepository
	orderRepo    domain.OrderRepository
	riskManager  *PortfolioRiskManager
	decision     *StrategyDecisionEngine
	executor     *TradeExecutor
	eventBus     DomainEventBus
	metrics      *Metrics
	stream       *streaming.TradeStreamManager
	klineStream  *streaming.KlineStreamManager
	log          zerolog.Logger

	tradeCh chan streaming.TradeEvent
	klineCh chan streaming.KlineEvent

	priceHistories  sync.Map // pair → *priceHistory
	lastClosedKline sync.Map

	activeMu sync.RWMutex
	active   []*domain.StrategyBinding

	inflightMu sync.Mutex
	inflight   map[string]struct{}

	indicatorRegistry *indicatorRegistry
}

func NewStrategyEvaluator(
	bindingRepo domain.StrategyBindingRepository,
	strategyRepo domain.StrategyRepository,
	positionRepo domain.PositionRepository,
	orderRepo domain.OrderRepository,
	riskManager *PortfolioRiskManager,
	decision *StrategyDecisionEngine,
	executor *TradeExecutor,
	eventBus DomainEventBus,
	metrics *Metrics,
	stream *streaming.TradeStreamManager,
	klineStream *streaming.KlineStreamManager,
	tradeCh chan streaming.TradeEvent,
	klineCh chan streaming.KlineEvent,
	log zerolog.Logger,
	indicatorRegistry *indicatorRegistry,
) *StrategyEvaluator {
	return &StrategyEvaluator{
		bindingRepo:       bindingRepo,
		strategyRepo:      strategyRepo,
		positionRepo:      positionRepo,
		orderRepo:         orderRepo,
		riskManager:       riskManager,
		decision:          decision,
		executor:          executor,
		eventBus:          eventBus,
		metrics:           metrics,
		stream:            stream,
		klineStream:       klineStream,
		log:               log.With().Str("component", "StrategyEvaluator").Logger(),
		tradeCh:           tradeCh,
		klineCh:           klineCh,
		inflight:          make(map[string]struct{}),
		indicatorRegistry: indicatorRegistry,
	}
}

func (e *StrategyEvaluator) Name() string { return "StrategyEvaluator" }

func (e *StrategyEvaluator) Run(ctx context.Context) error {
	if err := e.stream.Start(ctx); err != nil {
		return fmt.Errorf("启动 Trade 流: %w", err)
	}
	if err := e.klineStream.Start(ctx); err != nil {
		return fmt.Errorf("启动 K 线流: %w", err)
	}

	if err := e.refreshStrategyCache(ctx); err != nil {
		e.log.Warn().Err(err).Msg("首次策略缓存加载失败，将在收到事件后重试")
	}

	e.log.Info().Int("active_count", len(e.active)).Msg("StrategyEvaluator 启动")

	g, gctx := errgroup.WithContext(ctx)
	g.Go(func() error { return e.consumeTrade(gctx) })
	g.Go(func() error { return e.consumeKline(gctx) })
	return g.Wait()
}

func (e *StrategyEvaluator) Stop() {
	e.klineStream.Stop()
	e.stream.Stop()
	e.log.Info().Msg("StrategyEvaluator 已停止")
}

func (e *StrategyEvaluator) Refresh(ctx context.Context) error {
	if err := e.refreshStrategyCache(ctx); err != nil {
		return err
	}
	if err := e.stream.RefreshSubscriptions(ctx); err != nil {
		return err
	}
	return e.klineStream.RefreshSubscriptions(ctx)
}

func (e *StrategyEvaluator) refreshStrategyCache(ctx context.Context) error {
	bindings, err := e.bindingRepo.GetAllActive(ctx)
	if err != nil {
		return err
	}
	e.activeMu.Lock()
	e.active = bindings
	e.activeMu.Unlock()
	e.log.Debug().Int("count", len(bindings)).Msg("策略缓存已刷新")
	return nil
}

func (e *StrategyEvaluator) consumeTrade(ctx context.Context) error {
	for {
		select {
		case <-ctx.Done():
			return nil
		case evt, ok := <-e.tradeCh:
			if !ok {
				return nil
			}
			e.processTrade(ctx, evt)
		}
	}
}

func (e *StrategyEvaluator) consumeKline(ctx context.Context) error {
	for {
		select {
		case <-ctx.Done():
			return nil
		case evt, ok := <-e.klineCh:
			if !ok {
				return nil
			}
			e.processKline(ctx, evt)
		}
	}
}

func (e *StrategyEvaluator) getOrCreatePriceHistory(pair string) *priceHistory {
	val, _ := e.priceHistories.LoadOrStore(pair, &priceHistory{})
	return val.(*priceHistory)
}

func (e *StrategyEvaluator) appendPrice(ph *priceHistory, price decimal.Decimal) {
	ph.mu.Lock()
	ph.prices = append(ph.prices, price)
	if len(ph.prices) > maxPriceHistory {
		ph.prices = ph.prices[len(ph.prices)-maxPriceHistory:]
	}
	ph.mu.Unlock()
}

func (e *StrategyEvaluator) processTrade(ctx context.Context, evt streaming.TradeEvent) {
	pairUp := strings.ToUpper(evt.Pair)

	ph := e.getOrCreatePriceHistory(pairUp)
	e.appendPrice(ph, evt.Trade.Price)

	bindings := e.matchBindings(pairUp, evt.ExchangeID, "")
	if len(bindings) == 0 {
		return
	}

	traderGroups := groupByTrader(bindings)
	for _, group := range traderGroups {
		group := group
		for _, b := range group {
			b := b
			price := evt.Trade.Price
			ph.mu.Lock()
			pricesCopy := make([]decimal.Decimal, len(ph.prices))
			copy(pricesCopy, ph.prices)
			ph.mu.Unlock()

			window := KlineWindow{
				Prices: pricesCopy,
				Open:   price, High: price, Low: price, Close: price,
			}
			prevPrices := make([]decimal.Decimal, 0)
			if len(pricesCopy) > 1 {
				prevPrices = pricesCopy[:len(pricesCopy)-1]
			}
			prevWindow := KlineWindow{
				Prices: prevPrices,
				Open:   price, High: price, Low: price, Close: price,
			}
			e.evaluateBinding(ctx, b, pairUp, price, window, prevWindow, false)
		}
	}
}

func (e *StrategyEvaluator) processKline(ctx context.Context, evt streaming.KlineEvent) {
	pairUp := strings.ToUpper(evt.Pair)

	ph := e.getOrCreatePriceHistory(pairUp)
	e.appendPrice(ph, evt.Kline.Close)

	bindings := e.matchBindings(pairUp, evt.ExchangeID, evt.Interval)
	if len(bindings) == 0 {
		return
	}

	klineKey := fmt.Sprintf("%s|%s|%s", pairUp, evt.ExchangeID.String(), evt.Interval)

	ph.mu.Lock()
	pricesCopy := make([]decimal.Decimal, len(ph.prices))
	copy(pricesCopy, ph.prices)
	ph.mu.Unlock()

	var prevPrices []decimal.Decimal
	if len(pricesCopy) > 1 {
		prevPrices = pricesCopy[:len(pricesCopy)-1]
	}

	currentWindow := KlineWindow{
		Prices: pricesCopy,
		Open:   evt.Kline.Open, High: evt.Kline.High,
		Low: evt.Kline.Low, Close: evt.Kline.Close,
	}

	var prevWindow KlineWindow
	if lastRaw, ok := e.lastClosedKline.Load(klineKey); ok {
		last := lastRaw.(domain.Kline)
		prevWindow = KlineWindow{
			Prices: prevPrices,
			Open:   last.Open, High: last.High,
			Low: last.Low, Close: last.Close,
		}
	} else {
		prevWindow = currentWindow
	}
	e.lastClosedKline.Store(klineKey, evt.Kline)

	traderGroups := groupByTrader(bindings)
	for _, group := range traderGroups {
		group := group
		for _, b := range group {
			b := b
			e.evaluateBinding(ctx, b, pairUp, evt.Kline.Close, currentWindow, prevWindow, true)
		}
	}
}

func (e *StrategyEvaluator) matchBindings(pair string, exchangeID uuid.UUID, interval string) []*domain.StrategyBinding {
	e.activeMu.RLock()
	defer e.activeMu.RUnlock()

	var matched []*domain.StrategyBinding
	for _, b := range e.active {
		if b.ExchangeID != exchangeID {
			continue
		}
		found := false
		for _, p := range b.PairList() {
			if strings.EqualFold(p, pair) {
				found = true
				break
			}
		}
		if !found {
			continue
		}
		if interval != "" && b.EffectiveTimeframe() != interval {
			continue
		}
		matched = append(matched, b)
	}
	return matched
}

func groupByTrader(bindings []*domain.StrategyBinding) [][]*domain.StrategyBinding {
	groups := map[uuid.UUID][]*domain.StrategyBinding{}
	order := []uuid.UUID{}
	for _, b := range bindings {
		if _, ok := groups[b.TraderID]; !ok {
			order = append(order, b.TraderID)
		}
		groups[b.TraderID] = append(groups[b.TraderID], b)
	}
	result := make([][]*domain.StrategyBinding, len(order))
	for i, tid := range order {
		result[i] = groups[tid]
	}
	return result
}

func (e *StrategyEvaluator) evaluateBinding(ctx context.Context, b *domain.StrategyBinding, pair string, currentPrice decimal.Decimal, window, prevWindow KlineWindow, refreshPositionPrice bool) {
	positions, err := e.positionRepo.GetByStrategyID(ctx, b.ID)
	if err != nil {
		e.log.Error().Err(err).Str("binding_id", b.ID.String()).Msg("查询持仓失败")
		return
	}

	var openPositions []*domain.Position
	for _, p := range positions {
		if p.Status == domain.PositionStatusOpen && strings.EqualFold(p.Pair, pair) {
			openPositions = append(openPositions, p)
		}
	}
	hasOpen := len(openPositions) > 0

	strategy, err := e.strategyRepo.GetStrategy(ctx, b.StrategyID)
	if err != nil || strategy == nil {
		e.log.Warn().Err(err).Str("strategy_id", b.StrategyID.String()).Msg("读取策略模板失败")
		return
	}

	if refreshPositionPrice && currentPrice.IsPositive() {
		for _, pos := range openPositions {
			_ = pos.UpdateMarketPrice(currentPrice)
			_ = e.positionRepo.Update(ctx, pos)
		}
	}

	entryJSON := strategy.EntryCondition
	exitJSON := strategy.ExitCondition
	execJSON := strategy.ExecutionRule

	indicatorValues := e.indicatorRegistry.ComputeAll(window)
	previousValues := e.indicatorRegistry.ComputeAll(prevWindow)

	var avgEntry decimal.Decimal
	var qtyHeld decimal.Decimal
	lotCount := 0
	for _, p := range openPositions {
		qtyHeld = qtyHeld.Add(p.Quantity)
		avgEntry = avgEntry.Add(p.EntryPrice.Mul(p.Quantity))
		lotCount++
	}
	if qtyHeld.IsPositive() {
		avgEntry = avgEntry.Div(qtyHeld)
	}

	input := domain.DecisionInput{
		EntryCondition:    entryJSON,
		ExitCondition:     exitJSON,
		ExecutionRule:     execJSON,
		CurrentValues:     indicatorValues,
		PreviousValues:    previousValues,
		CurrentPrice:      currentPrice,
		AverageEntryPrice: avgEntry,
		QuantityHeld:      qtyHeld,
		LotCount:          lotCount,
	}
	decision := e.decision.Decide(input)

	switch decision.Action {
	case domain.ActionEnter:
		if !hasOpen {
			e.tryEnter(ctx, b, pair, currentPrice, decision)
		}
	case domain.ActionExit:
		for _, pos := range openPositions {
			e.closePosition(ctx, b, pair, pos, currentPrice)
		}
	case domain.ActionAddGrid:
		e.tryEnter(ctx, b, pair, currentPrice, decision)
	case domain.ActionReduceGrid:
		if len(openPositions) > 0 {
			e.closePosition(ctx, b, pair, openPositions[0], currentPrice)
		}
	}
}

func (e *StrategyEvaluator) tryEnter(ctx context.Context, b *domain.StrategyBinding, pair string, currentPrice decimal.Decimal, decision domain.StrategyDecision) {
	gateKey := b.ID.String() + "|" + pair
	e.inflightMu.Lock()
	if _, exists := e.inflight[gateKey]; exists {
		e.inflightMu.Unlock()
		e.log.Debug().Str("binding_id", b.ID.String()).Str("pair", pair).Msg("已有在途买单，跳过")
		return
	}
	e.inflight[gateKey] = struct{}{}
	e.inflightMu.Unlock()
	defer func() {
		e.inflightMu.Lock()
		delete(e.inflight, gateKey)
		e.inflightMu.Unlock()
	}()

	active, err := e.orderRepo.HasActiveBuy(ctx, b.ID, pair)
	if err == nil && active {
		e.log.Debug().Str("binding_id", b.ID.String()).Str("pair", pair).Msg("DB 存在在途买单，跳过")
		return
	}

	quoteSize := decision.QuoteSize
	if !quoteSize.IsPositive() {
		quoteSize = decimal.NewFromInt(100)
	}

	check := func(scope string) (bool, error) {
		var r RiskResult
		var err error
		if scope == "portfolio" {
			r, err = e.riskManager.Check(ctx, b.TraderID, b.ExchangeID)
		} else {
			r, err = e.riskManager.CheckPairRisk(ctx, b.TraderID, b.ExchangeID, pair, &quoteSize)
		}
		if err != nil {
			return false, err
		}
		if !r.IsAllowed {
			reason := strings.Join(r.DeniedReasons, "; ")
			e.log.Warn().Str("binding_id", b.ID.String()).Str("scope", scope).Str("reason", reason).Msg("风控拒绝入场")
			e.metrics.RiskDenials.Add(ctx, 1)
			if err := e.eventBus.Publish(ctx, RiskAlertPayload{
				AlertID:        uuid.New(),
				Level:          "Warning",
				Category:       "RiskCheck",
				TraderID:       b.TraderID,
				StrategyID:     &b.ID,
				Message:        reason,
				TriggeredAtUtc: time.Now().UTC(),
			}); err != nil {
				e.log.Error().Err(err).Msg("发布风控告警事件失败")
			}
			return false, nil
		}
		return true, nil
	}

	for _, scope := range []string{"portfolio", "pair"} {
		ok, err := check(scope)
		if err != nil {
			e.log.Error().Err(err).Str("scope", scope).Msg("风控检查出错")
			return
		}
		if !ok {
			return
		}
	}

	order := domain.NewAutoOrder(b.TraderID, b.ExchangeID, pair, domain.OrderSideBuy, quoteSize, b.ID, nil)
	result, exeErr := e.executor.ExecuteMarketOrder(ctx, order)
	if exeErr != nil {
		e.log.Error().Err(exeErr).Str("binding_id", b.ID.String()).Msg("下单异常")
		return
	}
	if result.Success {
		e.log.Info().Str("binding_id", b.ID.String()).Str("pair", pair).
			Str("qty", result.FilledQuantity.String()).Msg("买入成交")
		e.metrics.OrdersPlaced.Add(ctx, 1)
		if err := e.eventBus.Publish(ctx, OrderPlacedPayload{
			OrderID:     order.ID,
			TraderID:    b.TraderID,
			ExchangeID:  b.ExchangeID,
			StrategyID:  &b.ID,
			Pair:        pair,
			Side:        "Buy",
			Type:        "Market",
			Status:      string(order.Status),
			Quantity:    order.Quantity,
			PlacedAtUtc: order.PlacedAtUtc,
		}); err != nil {
			e.log.Error().Err(err).Msg("发布买入事件失败")
		}
	} else {
		e.log.Warn().Str("binding_id", b.ID.String()).Str("pair", pair).
			Str("error", result.Error).Msg("买入失败")
		e.metrics.OrdersRejected.Add(ctx, 1)
	}
}

func (e *StrategyEvaluator) closePosition(ctx context.Context, b *domain.StrategyBinding, pair string, pos *domain.Position, currentPrice decimal.Decimal) {
	sellOrder := domain.NewAutoOrder(b.TraderID, b.ExchangeID, pair, domain.OrderSideSell, pos.CurrentPrice.Mul(pos.Quantity), b.ID, &pos.ID)
	sellOrder.Quantity = pos.Quantity

	result, exeErr := e.executor.ExecuteMarketOrder(ctx, sellOrder)
	if exeErr != nil {
		e.log.Error().Err(exeErr).Str("position_id", pos.ID.String()).Msg("平仓异常")
		return
	}
	if result.Success {
		e.log.Info().Str("binding_id", b.ID.String()).Str("position_id", pos.ID.String()).
			Str("pair", pair).Msg("卖出平仓成交")
		e.metrics.OrdersPlaced.Add(ctx, 1)
		if err := e.eventBus.Publish(ctx, OrderPlacedPayload{
			OrderID:     sellOrder.ID,
			TraderID:    b.TraderID,
			ExchangeID:  b.ExchangeID,
			StrategyID:  &b.ID,
			Pair:        pair,
			Side:        "Sell",
			Type:        "Market",
			Status:      string(sellOrder.Status),
			Quantity:    sellOrder.Quantity,
			PlacedAtUtc: sellOrder.PlacedAtUtc,
		}); err != nil {
			e.log.Error().Err(err).Msg("发布卖出事件失败")
		}
	} else {
		e.metrics.OrdersRejected.Add(ctx, 1)
	}
}
