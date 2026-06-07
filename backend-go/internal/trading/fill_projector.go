package trading

import (
	"context"

	"github.com/google/uuid"
	"github.com/rs/zerolog"
	"github.com/shopspring/decimal"

	"tradex/internal/domain"
)

// FillProjector 「成交 → 持仓」投影器。对应 C# FillProjector。
// 持仓模型为"每笔买入成交一条 Position"；买入以 OpeningOrderId 幂等判重，
// 卖出定向（order.PositionID）或 FIFO 平仓。
type FillProjector struct {
	positionRepo domain.PositionRepository
	orderRepo    domain.OrderRepository
	eventBus     DomainEventBus
	log          zerolog.Logger
}

// NewFillProjector 构造投影器。
func NewFillProjector(positionRepo domain.PositionRepository, orderRepo domain.OrderRepository, eventBus DomainEventBus, log zerolog.Logger) *FillProjector {
	return &FillProjector{positionRepo: positionRepo, orderRepo: orderRepo, eventBus: eventBus, log: log}
}

// ProjectFilled 把一笔已成交订单投影到持仓。
func (p *FillProjector) ProjectFilled(ctx context.Context, order *domain.Order, avgFillPrice decimal.Decimal) error {
	if order.Status != domain.OrderStatusFilled || !order.FilledQuantity.IsPositive() {
		return nil
	}
	if order.Side == domain.OrderSideBuy {
		return p.projectBuy(ctx, order, avgFillPrice)
	}
	return p.projectSell(ctx, order, avgFillPrice)
}

func (p *FillProjector) projectBuy(ctx context.Context, order *domain.Order, avgFillPrice decimal.Decimal) error {
	// 幂等：该买单已开过仓 → 跳过
	existing, err := p.positionRepo.GetByOpeningOrderID(ctx, order.ID)
	if err != nil {
		return err
	}
	if existing != nil {
		p.log.Debug().Str("order_id", order.ID.String()).Msg("投影跳过：买单已存在对应持仓")
		return nil
	}

	entryPrice := resolveEntryPrice(order, avgFillPrice)
	if !entryPrice.IsPositive() {
		p.log.Warn().Str("order_id", order.ID.String()).Msg("投影失败：买单无法确定开仓价")
		return nil
	}

	strategyID := uuid.Nil
	if order.StrategyID != nil {
		strategyID = *order.StrategyID
	}
	position := domain.OpenPosition(order.TraderID, order.ExchangeID, strategyID, order.Pair, order.FilledQuantity, entryPrice)
	position.OpeningOrderID = &order.ID
	if err := p.positionRepo.Add(ctx, position); err != nil {
		return err
	}

	// 审计回链（幂等已由 OpeningOrderID 保证）
	order.PositionID = &position.ID
	if err := p.orderRepo.Update(ctx, order); err != nil {
		return err
	}

	p.log.Info().Str("order_id", order.ID.String()).Str("position_id", position.ID.String()).
		Str("pair", position.Pair).Msg("投影开仓")
	return p.publishPosition(ctx, position)
}

func (p *FillProjector) projectSell(ctx context.Context, order *domain.Order, avgFillPrice decimal.Decimal) error {
	// 定向平仓：卖单显式携带 PositionID
	if order.PositionID != nil {
		position, err := p.positionRepo.GetByID(ctx, *order.PositionID)
		if err != nil {
			return err
		}
		if position == nil {
			p.log.Warn().Str("order_id", order.ID.String()).Str("position_id", order.PositionID.String()).
				Msg("投影平仓：卖单关联持仓不存在")
			return nil
		}
		return p.closeOne(ctx, position, resolveExitPrice(order, avgFillPrice, position))
	}

	// FIFO 平仓：按开仓时间顺序平至覆盖成交量
	if order.StrategyID == nil {
		p.log.Warn().Str("order_id", order.ID.String()).Msg("投影平仓：卖单既无 PositionID 也无 StrategyID")
		return nil
	}
	open, err := p.positionRepo.GetOpenByStrategyAndPair(ctx, *order.StrategyID, order.Pair)
	if err != nil {
		return err
	}
	remaining := order.FilledQuantity
	for _, position := range open {
		if !remaining.IsPositive() {
			break
		}
		if err := p.closeOne(ctx, position, resolveExitPrice(order, avgFillPrice, position)); err != nil {
			return err
		}
		remaining = remaining.Sub(position.Quantity)
	}
	if remaining.IsPositive() {
		p.log.Warn().Str("order_id", order.ID.String()).Str("gap", remaining.String()).
			Msg("投影平仓：卖单成交量超过在手持仓（疑似持仓/余额漂移，待持仓级对账）")
	}
	return nil
}

func (p *FillProjector) closeOne(ctx context.Context, position *domain.Position, exitPrice decimal.Decimal) error {
	if position.Status != domain.PositionStatusOpen {
		return nil
	}
	if err := position.Close(exitPrice); err != nil {
		return err
	}
	if err := p.positionRepo.Update(ctx, position); err != nil {
		return err
	}
	p.log.Info().Str("position_id", position.ID.String()).Str("pair", position.Pair).
		Str("pnl", position.RealizedPnl.String()).Msg("投影平仓")
	return p.publishPosition(ctx, position)
}

func (p *FillProjector) publishPosition(ctx context.Context, pos *domain.Position) error {
	return p.eventBus.Publish(ctx, PositionUpdatedPayload{
		PositionID:    pos.ID,
		TraderID:      pos.TraderID,
		ExchangeID:    pos.ExchangeID,
		StrategyID:    pos.StrategyID,
		Pair:          pos.Pair,
		Quantity:      pos.Quantity,
		EntryPrice:    pos.EntryPrice,
		UnrealizedPnl: pos.UnrealizedPnl,
		RealizedPnl:   pos.RealizedPnl,
		Status:        string(pos.Status),
		UpdatedAt:     pos.UpdatedAt,
	})
}

// resolveEntryPrice 开仓价：成交均价优先，退化到 quote 金额/成交量，再退化到委托价。
func resolveEntryPrice(order *domain.Order, avgFillPrice decimal.Decimal) decimal.Decimal {
	if avgFillPrice.IsPositive() {
		return avgFillPrice
	}
	if order.QuoteQuantity.IsPositive() && order.FilledQuantity.IsPositive() {
		return order.QuoteQuantity.Div(order.FilledQuantity)
	}
	if order.Price != nil {
		return *order.Price
	}
	return decimal.Zero
}

// resolveExitPrice 平仓价：成交均价优先，退化到委托价，再退化到持仓最后已知市价/开仓价。
func resolveExitPrice(order *domain.Order, avgFillPrice decimal.Decimal, position *domain.Position) decimal.Decimal {
	if avgFillPrice.IsPositive() {
		return avgFillPrice
	}
	if order.Price != nil && order.Price.IsPositive() {
		return *order.Price
	}
	if position.CurrentPrice.IsPositive() {
		return position.CurrentPrice
	}
	return position.EntryPrice
}
