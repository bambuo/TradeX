package trading

import (
	"context"
	"fmt"
	"strings"
	"sync"
	"time"

	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

var pairReplacer = strings.NewReplacer("-", "", "_", "", "/", "")

type clientFactory interface {
	CreateClient(t domain.ExchangeType, apiKey, secretKey string, passphrase *string) (exchange.Client, error)
}

type decryptor interface {
	Decrypt(ciphertext string) (string, error)
}

type TradeExecutor struct {
	exchangeRepo domain.ExchangeRepository
	orderRepo    domain.OrderRepository
	fillProj     *FillProjector
	factory      clientFactory
	enc          decryptor
	settings     RiskSettings
	log          zerolog.Logger

	pairRules *pairRuleCache
	slippage  *orderBookSlippageGuard
}

func NewTradeExecutor(
	exchangeRepo domain.ExchangeRepository,
	orderRepo domain.OrderRepository,
	fillProj *FillProjector,
	factory clientFactory,
	enc decryptor,
	settings RiskSettings,
	log zerolog.Logger,
) *TradeExecutor {
	return &TradeExecutor{
		exchangeRepo: exchangeRepo,
		orderRepo:    orderRepo,
		fillProj:     fillProj,
		factory:      factory,
		enc:          enc,
		settings:     settings,
		log:          log.With().Str("component", "TradeExecutor").Logger(),
		pairRules:    newPairRuleCache(),
		slippage:     newOrderBookSlippageGuard(),
	}
}

func (e *TradeExecutor) ExecuteMarketOrder(ctx context.Context, order *domain.Order) (exchange.OrderResult, error) {
	return e.execute(ctx, order, false)
}

func (e *TradeExecutor) execute(ctx context.Context, order *domain.Order, isLimit bool) (exchange.OrderResult, error) {
	order.Status = domain.OrderStatusPending
	order.UpdatedAt = time.Now().UTC()

	if err := e.orderRepo.Add(ctx, order); err != nil {
		return exchange.OrderResult{Success: false, Error: fmt.Sprintf("订单入库失败: %v", err)}, err
	}

	ex, err := e.exchangeRepo.GetByID(ctx, order.ExchangeID)
	if err != nil || ex == nil {
		_ = order.MarkFailed("交易所不存在")
		_ = e.orderRepo.Update(ctx, order)
		return exchange.OrderResult{Success: false, Error: "交易所不存在"}, err
	}

	client, err := e.createClient(ctx, ex)
	if err != nil {
		_ = order.MarkFailed(fmt.Sprintf("创建客户端失败: %v", err))
		_ = e.orderRepo.Update(ctx, order)
		return exchange.OrderResult{Success: false, Error: err.Error()}, err
	}

	baseQty, prepErr := e.prepareQuantity(ctx, client, order, ex.Type)
	if prepErr != nil {
		_ = order.MarkFailed(prepErr.Error())
		_ = e.orderRepo.Update(ctx, order)
		return exchange.OrderResult{Success: false, Error: prepErr.Error()}, prepErr
	}
	order.Quantity = baseQty

	req := exchange.OrderRequest{
		Pair:          order.Pair,
		Side:          order.Side,
		Type:          order.Type,
		Quantity:      baseQty,
		ClientOrderID: order.ClientOrderID.String(),
	}

	result, err := client.PlaceOrder(ctx, req)
	if err != nil {
		e.log.Error().Err(err).Str("order_id", order.ID.String()).Msg("下单调用交易所异常，保留 Pending 等待对账")
		return exchange.OrderResult{Success: false, Error: fmt.Sprintf("下单异常: %v", err)}, nil
	}

	if result.Success {
		var (
			feeAsset  *string
			exchOrder *string
		)
		if result.FeeAsset != "" {
			feeAsset = &result.FeeAsset
		}
		if result.ExchangeOrderID != "" {
			exchOrder = &result.ExchangeOrderID
		}
		_ = order.RecordFill(result.FilledQuantity, result.Fee, exchOrder, feeAsset)
	} else {
		_ = order.MarkFailed(result.Error)
	}
	if err := e.orderRepo.Update(ctx, order); err != nil {
		e.log.Error().Err(err).Str("order_id", order.ID.String()).Msg("订单 post-update 失败")
	}

	if order.Status == domain.OrderStatusFilled {
		if projErr := e.fillProj.ProjectFilled(ctx, order, result.AvgPrice); projErr != nil {
			e.log.Error().Err(projErr).Str("order_id", order.ID.String()).Msg("成交→持仓投影失败")
		}
	}

	return result, nil
}

func (e *TradeExecutor) prepareQuantity(ctx context.Context, client exchange.Client, order *domain.Order, exType domain.ExchangeType) (decimal.Decimal, error) {
	var book *exchange.OrderBook
	if order.Type == domain.OrderTypeMarket {
		b, err := client.GetOrderBook(ctx, order.Pair, 50)
		if err == nil {
			book = &b
		}
	}

	refPrice := decimal.Zero
	if order.Type == domain.OrderTypeMarket && book != nil {
		if order.Side == domain.OrderSideBuy && len(book.Asks) > 0 {
			refPrice = book.Asks[0].Price
		} else if order.Side == domain.OrderSideSell && len(book.Bids) > 0 {
			refPrice = book.Bids[0].Price
		}
	}
	if order.Price != nil && order.Price.IsPositive() {
		refPrice = *order.Price
	}

	var baseQty decimal.Decimal
	if order.Side == domain.OrderSideSell {
		if !order.Quantity.IsPositive() {
			return decimal.Zero, fmt.Errorf("卖单数量无效")
		}
		baseQty = order.Quantity
	} else {
		quote := order.QuoteQuantity
		if !quote.IsPositive() {
			if order.Quantity.IsPositive() {
				return order.Quantity, nil
			}
			return decimal.Zero, fmt.Errorf("买单金额无效")
		}
		if !refPrice.IsPositive() {
			return decimal.Zero, fmt.Errorf("无法获取参考价，拒绝市价买单")
		}
		baseQty = quote.Div(refPrice)
		if !baseQty.IsPositive() {
			return decimal.Zero, fmt.Errorf("换算后数量无效")
		}
	}

	rule, err := e.pairRules.Get(ctx, client, order.Pair)
	if err == nil && rule != nil {
		if rule.StepSize.IsPositive() {
			step := rule.StepSize
			rounded := baseQty.Div(step).Floor().Mul(step)
			if !rounded.IsPositive() {
				return decimal.Zero, fmt.Errorf("数量按步进 %s 取整后为 0（原始 %s）", step.String(), baseQty.String())
			}
			baseQty = rounded
		}
		if rule.MinQuantity.IsPositive() && baseQty.LessThan(rule.MinQuantity) {
			return decimal.Zero, fmt.Errorf("数量 %s 低于最小下单量 %s", baseQty.String(), rule.MinQuantity.String())
		}
		if rule.MinNotional.IsPositive() && refPrice.IsPositive() {
			notional := baseQty.Mul(refPrice)
			if notional.LessThan(rule.MinNotional) {
				return decimal.Zero, fmt.Errorf("名义价值 %s 低于最小下单额 %s", notional.StringFixed(2), rule.MinNotional.StringFixed(2))
			}
		}
	}

	if order.Type == domain.OrderTypeMarket && e.settings.MaxSlippagePercent.IsPositive() && book != nil && refPrice.IsPositive() {
		est := e.slippage.Estimate(*book, order.Side, baseQty, refPrice)
		if !est.Sufficient {
			return decimal.Zero, fmt.Errorf("订单簿深度不足: %s", est.Reason)
		}
		if est.SlippagePercent.GreaterThan(e.settings.MaxSlippagePercent) {
			return decimal.Zero, fmt.Errorf("预估滑点 %s%% 超过上限 %s%%", est.SlippagePercent.StringFixed(2), e.settings.MaxSlippagePercent.StringFixed(2))
		}
	}

	return baseQty, nil
}

func (e *TradeExecutor) createClient(ctx context.Context, ex *domain.Exchange) (exchange.Client, error) {
	apiKey, err := e.enc.Decrypt(ex.APIKeyEncrypted)
	if err != nil {
		return nil, fmt.Errorf("解密 apiKey: %w", err)
	}
	secretKey, err := e.enc.Decrypt(ex.SecretKeyEncrypted)
	if err != nil {
		return nil, fmt.Errorf("解密 secretKey: %w", err)
	}
	var passphrase *string
	if ex.PassphraseEncrypted != nil {
		pp, err := e.enc.Decrypt(*ex.PassphraseEncrypted)
		if err != nil {
			return nil, fmt.Errorf("解密 passphrase: %w", err)
		}
		passphrase = &pp
	}
	return e.factory.CreateClient(ex.Type, apiKey, secretKey, passphrase)
}

type pairRuleCache struct {
	mu    sync.Mutex
	ttl   time.Duration
	store map[domain.ExchangeType]*cachedRules
}

type cachedRules struct {
	byPair   map[string]exchange.PairRule
	loadedAt time.Time
}

func newPairRuleCache() *pairRuleCache {
	return &pairRuleCache{
		ttl:   time.Hour,
		store: make(map[domain.ExchangeType]*cachedRules),
	}
}

func (c *pairRuleCache) Get(ctx context.Context, client exchange.Client, pair string) (*exchange.PairRule, error) {
	exType := client.Type()
	c.mu.Lock()
	entry, ok := c.store[exType]
	c.mu.Unlock()

	if ok && time.Since(entry.loadedAt) < c.ttl {
		if rule, found := entry.byPair[normalizePair(pair)]; found {
			return &rule, nil
		}
		return nil, nil
	}

	rules, err := client.GetPairRules(ctx)
	if err != nil {
		return nil, err
	}
	byPair := make(map[string]exchange.PairRule, len(rules))
	for _, r := range rules {
		byPair[normalizePair(r.Pair)] = r
	}
	entry = &cachedRules{byPair: byPair, loadedAt: time.Now()}
	c.mu.Lock()
	c.store[exType] = entry
	c.mu.Unlock()

	if rule, found := byPair[normalizePair(pair)]; found {
		return &rule, nil
	}
	return nil, nil
}

func normalizePair(pair string) string {
	return strings.ToUpper(pairReplacer.Replace(pair))
}

type slippageEstimate struct {
	Sufficient      bool
	SlippagePercent decimal.Decimal
	Reason          string
}

type orderBookSlippageGuard struct{}

func newOrderBookSlippageGuard() *orderBookSlippageGuard {
	return &orderBookSlippageGuard{}
}

func (g *orderBookSlippageGuard) Estimate(book exchange.OrderBook, side domain.OrderSide, quantity, refPrice decimal.Decimal) slippageEstimate {
	if !quantity.IsPositive() || !refPrice.IsPositive() {
		return slippageEstimate{Sufficient: true}
	}

	var levels []exchange.OrderBookLevel
	if side == domain.OrderSideBuy {
		levels = book.Asks
	} else {
		levels = book.Bids
	}

	if len(levels) == 0 {
		return slippageEstimate{Sufficient: false, Reason: "订单簿为空"}
	}

	remaining := quantity
	totalCost := decimal.Zero
	totalQty := decimal.Zero
	const maxLevels = 20

	for i := 0; i < len(levels) && i < maxLevels && remaining.IsPositive(); i++ {
		lvl := levels[i]
		if !lvl.Quantity.IsPositive() || !lvl.Price.IsPositive() {
			continue
		}
		take := decimal.Min(remaining, lvl.Quantity)
		totalCost = totalCost.Add(take.Mul(lvl.Price))
		totalQty = totalQty.Add(take)
		remaining = remaining.Sub(take)
	}

	if remaining.IsPositive() {
		return slippageEstimate{Sufficient: false, Reason: fmt.Sprintf("深度不足: 还需 %.4f", remaining.InexactFloat64())}
	}

	avgPrice := totalCost.Div(totalQty)
	slippagePct := avgPrice.Sub(refPrice).Abs().Div(refPrice).Mul(decimal.NewFromInt(100))
	return slippageEstimate{
		Sufficient:      true,
		SlippagePercent: slippagePct,
	}
}
