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
)

type klineSubInfo struct {
	exchangeID   uuid.UUID
	exchangeType domain.ExchangeType
	pair         string
	interval     string
}

type klineSub struct {
	info   klineSubInfo
	cancel context.CancelFunc
}

// KlineStreamManager K 线流订阅管理器。检测 K 线收盘（OpenTime 变化）后推送事件。
// 对应 C# KlineStreamManager。
type KlineStreamManager struct {
	bindingRepo  domain.StrategyBindingRepository
	exchangeRepo domain.ExchangeRepository
	factory      PublicClientFactory
	out          chan KlineEvent
	log          zerolog.Logger

	mu         sync.Mutex
	subs       map[string]*klineSub
	rootCtx    context.Context
	rootCancel context.CancelFunc
}

// NewKlineStreamManager 构造 K 线流管理器。
func NewKlineStreamManager(
	bindingRepo domain.StrategyBindingRepository,
	exchangeRepo domain.ExchangeRepository,
	factory PublicClientFactory,
	out chan KlineEvent,
	log zerolog.Logger,
) *KlineStreamManager {
	return &KlineStreamManager{
		bindingRepo: bindingRepo, exchangeRepo: exchangeRepo, factory: factory,
		out: out, log: log, subs: map[string]*klineSub{},
	}
}

// Start 建立所有订阅。
func (m *KlineStreamManager) Start(ctx context.Context) error {
	m.mu.Lock()
	m.rootCtx, m.rootCancel = context.WithCancel(ctx)
	m.mu.Unlock()
	if err := m.RefreshSubscriptions(ctx); err != nil {
		return err
	}
	m.mu.Lock()
	n := len(m.subs)
	m.mu.Unlock()
	m.log.Info().Int("count", n).Msg("KlineStreamManager 启动")
	return nil
}

// Stop 断开所有订阅。
func (m *KlineStreamManager) Stop() {
	m.mu.Lock()
	if m.rootCancel != nil {
		m.rootCancel()
	}
	for k, s := range m.subs {
		s.cancel()
		delete(m.subs, k)
	}
	m.mu.Unlock()
	m.log.Info().Msg("KlineStreamManager 已停止")
}

// RefreshSubscriptions 重新计算订阅集合。
func (m *KlineStreamManager) RefreshSubscriptions(ctx context.Context) error {
	bindings, err := m.bindingRepo.GetAllActive(ctx)
	if err != nil {
		return err
	}
	needed := map[string]klineSubInfo{}
	for _, b := range bindings {
		tf := b.EffectiveTimeframe()
		for _, pair := range b.PairList() {
			key := klineKey(b.ExchangeID, pair, tf)
			if _, ok := needed[key]; !ok {
				needed[key] = klineSubInfo{b.ExchangeID, resolveExchangeType(ctx, m.exchangeRepo, b.ExchangeID), pair, tf}
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
			m.log.Info().Str("key", k).Msg("K 线订阅已断开")
		}
	}
	for k, info := range needed {
		if _, ok := m.subs[k]; ok {
			continue
		}
		subCtx, cancel := context.WithCancel(m.rootCtx)
		m.subs[k] = &klineSub{info: info, cancel: cancel}
		go m.runLoop(subCtx, k, info)
		m.log.Info().Str("exchange_id", info.exchangeID.String()).Str("pair", info.pair).
			Str("interval", info.interval).Msg("K 线订阅已建立")
	}
	return nil
}

func (m *KlineStreamManager) runLoop(ctx context.Context, key string, info klineSubInfo) {
	var backoffSec int64 = int64(streamInitialBackoff / time.Second)

	for ctx.Err() == nil {
		client, err := m.factory.CreatePublicClient(info.exchangeType)
		if err != nil {
			m.log.Warn().Err(err).Str("key", key).Msg("创建 K 线客户端失败")
		} else {
			// 每条连接独立的收盘检测状态。
			var lastOpen time.Time
			var hasLast bool
			var lastKline domain.Kline

			err = client.SubscribeKlines(ctx, info.pair, info.interval, func(c domain.Kline) {
				atomic.StoreInt64(&backoffSec, int64(streamInitialBackoff/time.Second))
				// 相同 OpenTime → 同一根 K 线更新，跳过。
				if hasLast && c.Timestamp.Equal(lastOpen) {
					return
				}
				// 首根：缓存等下一根判断收盘。
				if !hasLast {
					lastOpen, lastKline, hasLast = c.Timestamp, c, true
					return
				}
				// OpenTime 变化 → 前一根已收盘 → 推送。
				dropOldestSend(m.out, KlineEvent{Pair: info.pair, ExchangeType: info.exchangeType, ExchangeID: info.exchangeID, Interval: info.interval, Kline: lastKline})
				lastOpen, lastKline = c.Timestamp, c
			})
		}
		if ctx.Err() != nil {
			return
		}
		d := time.Duration(atomic.LoadInt64(&backoffSec)) * time.Second
		m.log.Warn().Err(err).Str("key", key).Dur("retry", d).Msg("K 线流断开, 重连")
		if sleepCtx(ctx, d) != nil {
			return
		}
		next := min(atomic.LoadInt64(&backoffSec)*2, int64(streamMaxBackoff/time.Second))
		atomic.StoreInt64(&backoffSec, next)
	}
}

func klineKey(exchangeID uuid.UUID, pair, interval string) string {
	return strings.ReplaceAll(exchangeID.String(), "-", "") + ":" + strings.ToUpper(pair) + ":" + strings.ToLower(interval)
}
