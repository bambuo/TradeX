package streaming

import (
	"context"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"

	"tradex/internal/domain"
	"tradex/internal/infra/exchange"
)

const (
	streamInitialBackoff = time.Second
	streamMaxBackoff     = 30 * time.Second
)

type tradeSubInfo struct {
	exchangeID   uuid.UUID
	exchangeType domain.ExchangeType
	pair         string
}

type tradeSub struct {
	info   tradeSubInfo
	cancel context.CancelFunc
}

// TradeStreamManager 逐笔成交订阅管理器。生命周期由 StrategyEvaluationConsumer 驱动。
// 对应 C# TradeStreamManager。
type TradeStreamManager struct {
	bindingRepo  domain.StrategyBindingRepository
	exchangeRepo domain.ExchangeRepository
	factory      PublicClientFactory
	out          chan TradeEvent
	log          zerolog.Logger

	mu         sync.Mutex
	subs       map[string]*tradeSub
	rootCtx    context.Context
	rootCancel context.CancelFunc
}

// NewTradeStreamManager 构造 Trade 流管理器。out 为共享的有界事件通道。
func NewTradeStreamManager(
	bindingRepo domain.StrategyBindingRepository,
	exchangeRepo domain.ExchangeRepository,
	factory PublicClientFactory,
	out chan TradeEvent,
	log zerolog.Logger,
) *TradeStreamManager {
	return &TradeStreamManager{
		bindingRepo: bindingRepo, exchangeRepo: exchangeRepo, factory: factory,
		out: out, log: log, subs: map[string]*tradeSub{},
	}
}

// Start 建立所有订阅。
func (m *TradeStreamManager) Start(ctx context.Context) error {
	m.mu.Lock()
	m.rootCtx, m.rootCancel = context.WithCancel(ctx)
	m.mu.Unlock()
	if err := m.RefreshSubscriptions(ctx); err != nil {
		return err
	}
	m.mu.Lock()
	n := len(m.subs)
	m.mu.Unlock()
	m.log.Info().Int("count", n).Msg("TradeStreamManager 启动")
	return nil
}

// Stop 断开所有订阅。
func (m *TradeStreamManager) Stop() {
	m.mu.Lock()
	if m.rootCancel != nil {
		m.rootCancel()
	}
	for k, s := range m.subs {
		s.cancel()
		delete(m.subs, k)
	}
	m.mu.Unlock()
	m.log.Info().Msg("TradeStreamManager 已停止")
}

// RefreshSubscriptions 重新计算订阅集合：新增的启动、移除的断开、已有的保留。
func (m *TradeStreamManager) RefreshSubscriptions(ctx context.Context) error {
	bindings, err := m.bindingRepo.GetAllActive(ctx)
	if err != nil {
		return err
	}
	needed := map[string]tradeSubInfo{}
	for _, b := range bindings {
		for _, pair := range b.PairList() {
			key := tradeKey(b.ExchangeID, pair)
			if _, ok := needed[key]; !ok {
				needed[key] = tradeSubInfo{b.ExchangeID, resolveExchangeType(ctx, m.exchangeRepo, b.ExchangeID), pair}
			}
		}
	}

	m.mu.Lock()
	defer m.mu.Unlock()
	if m.rootCtx == nil {
		return nil
	}

	for k, s := range m.subs {
		if _, ok := needed[k]; !ok {
			s.cancel()
			delete(m.subs, k)
			m.log.Info().Str("key", k).Msg("Trade 订阅已断开")
		}
	}
	for k, info := range needed {
		if _, ok := m.subs[k]; ok {
			continue
		}
		subCtx, cancel := context.WithCancel(m.rootCtx)
		m.subs[k] = &tradeSub{info: info, cancel: cancel}
		go m.runLoop(subCtx, k, info)
		m.log.Info().Str("exchange_id", info.exchangeID.String()).Str("pair", info.pair).Msg("Trade 订阅已建立")
	}
	return nil
}

func (m *TradeStreamManager) runLoop(ctx context.Context, key string, info tradeSubInfo) {
	var backoffSec int64 = int64(streamInitialBackoff / time.Second)

	for ctx.Err() == nil {
		client, err := m.factory.CreatePublicClient(info.exchangeType)
		if err != nil {
			m.log.Warn().Err(err).Str("key", key).Msg("创建 Trade 客户端失败")
		} else {
			err = client.SubscribeTrades(ctx, info.pair, func(t exchange.Trade) {
				atomic.StoreInt64(&backoffSec, int64(streamInitialBackoff/time.Second))
				dropOldestSend(m.out, TradeEvent{Pair: info.pair, ExchangeType: info.exchangeType, ExchangeID: info.exchangeID, Trade: t})
			})
		}
		if ctx.Err() != nil {
			return
		}
		d := time.Duration(atomic.LoadInt64(&backoffSec)) * time.Second
		m.log.Warn().Err(err).Str("key", key).Dur("retry", d).Msg("Trade 流断开, 重连")
		if sleepCtx(ctx, d) != nil {
			return
		}
		next := min(atomic.LoadInt64(&backoffSec)*2, int64(streamMaxBackoff/time.Second))
		atomic.StoreInt64(&backoffSec, next)
	}
}

func tradeKey(exchangeID uuid.UUID, pair string) string {
	return strings.ReplaceAll(exchangeID.String(), "-", "") + ":" + strings.ToUpper(pair)
}

func sleepCtx(ctx context.Context, d time.Duration) error {
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-t.C:
		return nil
	}
}
