package worker

import (
	"context"
	"errors"
	"fmt"
	"strings"
	"time"

	"github.com/rs/zerolog"

	"tradex/internal/domain"
	"tradex/internal/infra/crypto"
	"tradex/internal/infra/exchange"
)

const (
	syncInterval     = 5 * time.Minute
	syncInitialDelay = 30 * time.Second
	orderHistoryLim  = 200
)

// ClientFactory 按交易所类型构造客户端。*exchange.Factory 即满足此接口；
// 抽成接口便于测试注入 fake。
type ClientFactory interface {
	CreateClient(t domain.ExchangeType, apiKey, secretKey string, passphrase *string) (exchange.Client, error)
}

// ExchangeOrderSync 周期性从各启用交易所拉取历史订单并 upsert 到本地。
// 对应 C# TradeX.Worker.ExchangeOrderSyncService。
type ExchangeOrderSync struct {
	exchangeRepo domain.ExchangeRepository
	historyRepo  domain.ExchangeOrderHistoryRepository
	enc          *crypto.Service
	factory      ClientFactory
	log          zerolog.Logger
}

// NewExchangeOrderSync 构造历史订单同步服务。
func NewExchangeOrderSync(
	exchangeRepo domain.ExchangeRepository,
	historyRepo domain.ExchangeOrderHistoryRepository,
	enc *crypto.Service,
	factory ClientFactory,
	log zerolog.Logger,
) *ExchangeOrderSync {
	return &ExchangeOrderSync{
		exchangeRepo: exchangeRepo,
		historyRepo:  historyRepo,
		enc:          enc,
		factory:      factory,
		log:          log.With().Str("service", "ExchangeOrderSync").Logger(),
	}
}

func (s *ExchangeOrderSync) Name() string { return "ExchangeOrderSync" }

func (s *ExchangeOrderSync) Run(ctx context.Context) error {
	s.log.Info().Float64("delay_s", syncInitialDelay.Seconds()).Msg("ExchangeOrderSync 将在首次延迟后执行")
	if err := sleep(ctx, syncInitialDelay); err != nil {
		return err
	}

	for {
		start := time.Now()
		if err := s.syncAll(ctx); err != nil && !errors.Is(err, context.Canceled) {
			s.log.Error().Err(err).Msg("同步历史订单时发生未预期异常")
		}
		if ctx.Err() != nil {
			return nil
		}
		s.log.Info().
			Str("elapsed_s", fmt.Sprintf("%.1f", time.Since(start).Seconds())).
			Float64("interval_m", syncInterval.Minutes()).
			Msg("本轮同步完成，等待下一轮")
		if err := sleep(ctx, syncInterval); err != nil {
			return err
		}
	}
}

func (s *ExchangeOrderSync) syncAll(ctx context.Context) error {
	exchanges, err := s.exchangeRepo.GetAllEnabled(ctx)
	if err != nil {
		return fmt.Errorf("查询启用交易所: %w", err)
	}
	s.log.Info().Int("count", len(exchanges)).Msg("开始同步已启用交易所的历史订单")

	for _, ex := range exchanges {
		if ctx.Err() != nil {
			return ctx.Err()
		}
		if err := s.syncOne(ctx, ex); err != nil && !errors.Is(err, context.Canceled) {
			s.log.Error().Err(err).Str("name", ex.Name).Str("id", ex.ID.String()).
				Msg("同步交易所历史订单失败")
		}
	}
	return nil
}

func (s *ExchangeOrderSync) syncOne(ctx context.Context, ex *domain.Exchange) error {
	apiKey, err := s.enc.Decrypt(ex.APIKeyEncrypted)
	if err != nil {
		return fmt.Errorf("解密 apiKey: %w", err)
	}
	secretKey, err := s.enc.Decrypt(ex.SecretKeyEncrypted)
	if err != nil {
		return fmt.Errorf("解密 secretKey: %w", err)
	}
	var passphrase *string
	if ex.PassphraseEncrypted != nil {
		pp, err := s.enc.Decrypt(*ex.PassphraseEncrypted)
		if err != nil {
			return fmt.Errorf("解密 passphrase: %w", err)
		}
		passphrase = &pp
	}

	client, err := s.factory.CreateClient(ex.Type, apiKey, secretKey, passphrase)
	if err != nil {
		return err
	}

	balances, err := client.GetAssetBalances(ctx)
	if err != nil {
		return fmt.Errorf("获取资产余额: %w", err)
	}

	// 推断可交易资产（非 USDT）。
	tradingCurrencies := make([]string, 0, len(balances))
	for currency := range balances {
		if !strings.EqualFold(currency, "USDT") {
			tradingCurrencies = append(tradingCurrencies, currency)
		}
	}
	s.log.Debug().Str("name", ex.Name).Int("count", len(tradingCurrencies)).Msg("发现可交易资产")

	var allOrders []*domain.ExchangeOrderHistory
	now := time.Now().UTC()
	for _, currency := range tradingCurrencies {
		if ctx.Err() != nil {
			return ctx.Err()
		}
		pair := formatPair(currency, ex.Type)
		orders, err := client.GetOrderHistoryByPair(ctx, pair, orderHistoryLim)
		if err != nil {
			if errors.Is(err, context.Canceled) {
				return err
			}
			s.log.Warn().Err(err).Str("name", ex.Name).Str("currency", currency).
				Msg("同步交易对历史订单失败")
			continue
		}
		for _, o := range orders {
			allOrders = append(allOrders, &domain.ExchangeOrderHistory{
				ExchangeID:      ex.ID,
				Pair:            o.Pair,
				Side:            domain.OrderSide(o.Side),
				Type:            domain.OrderType(o.Type),
				Status:          domain.OrderStatus(o.Status),
				Price:           o.Price,
				Quantity:        o.Quantity,
				FilledQuantity:  o.FilledQuantity,
				ExchangeOrderID: o.ExchangeOrderID,
				PlacedAt:        o.PlacedAt,
				SyncedAt:        now,
			})
		}
	}

	if len(allOrders) > 0 {
		if err := s.historyRepo.UpsertMany(ctx, allOrders); err != nil {
			return fmt.Errorf("upsert 历史订单: %w", err)
		}
		s.log.Debug().Str("name", ex.Name).Int("count", len(allOrders)).Msg("交易所同步完成")
	} else {
		s.log.Debug().Str("name", ex.Name).Msg("交易所无新历史订单")
	}
	return nil
}

// formatPair 按交易所类型格式化交易对：OKX "BTC-USDT"；Gate "BTC_USDT"；其余 "BTCUSDT"。
// 对应 C# ExchangeOrderSyncService.FormatPair。
func formatPair(currency string, t domain.ExchangeType) string {
	switch t {
	case domain.ExchangeTypeOKX:
		return currency + "-USDT"
	case domain.ExchangeTypeGate:
		return currency + "_USDT"
	default:
		return currency + "USDT"
	}
}

// sleep 等待 d 或 ctx 取消，取消时返回 ctx.Err()。
func sleep(ctx context.Context, d time.Duration) error {
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-t.C:
		return nil
	}
}
