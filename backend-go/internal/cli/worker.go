package cli

import (
	"context"
	"database/sql"
	"os"
	"os/signal"
	"syscall"

	_ "github.com/jackc/pgx/v5/stdlib"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"

	"tradex/internal/infra/crypto"
	"tradex/internal/infra/exchange"
	guard "tradex/internal/infra/guard"
	"tradex/internal/infra/persistence"
	"tradex/internal/infra/telemetry"
	"tradex/internal/trading"
	"tradex/internal/trading/streaming"
	"tradex/internal/worker"
)

// NewWorkerCmd 实盘交易 Worker 进程入口。对应 C# TradeX.Worker。
func NewWorkerCmd() *cobra.Command {
	cmd := &cobra.Command{
		Use:           "worker",
		Short:         "TradeX 实盘交易 Worker",
		SilenceUsage:  true,
		SilenceErrors: true,
		RunE: func(_ *cobra.Command, _ []string) error {
			log := zerolog.New(os.Stdout).With().Timestamp().Logger()

			dsn := viper.GetString("database_dsn")
			redisAddr := viper.GetString("redis_addr")
			otlpEndpoint := viper.GetString("otlp_endpoint")
			encryptionKey := viper.GetString("encryption_key")

			log.Info().Str("dsn", maskDSN(dsn)).Bool("redis", redisAddr != "").Msg("TradeX.Worker 启动")

			// ── 加密服务（解密交易所密钥，须与 API 进程同密钥）──
			if encryptionKey == "" {
				return errMissing("encryption_key")
			}
			encSvc, err := crypto.NewService(encryptionKey)
			if err != nil {
				return err
			}

			// ── 遥测 ──
			shutdown, err := telemetry.InitOTel(context.Background(), telemetry.Config{
				ServiceName:    "tradex-worker",
				ServiceVersion: "1.0.0",
				OTLPEndpoint:   otlpEndpoint,
				Environment:    viper.GetString("environment"),
			})
			if err != nil {
				log.Warn().Err(err).Msg("遥测初始化失败，无追踪继续运行")
			} else {
				defer shutdown(context.Background())
			}

			// ── 数据库 ──
			// 注意：schema 由 C# EF 迁移拥有（Go 与 C# 共享同一库），Worker 不做 AutoMigrate，
			// 避免向共享表追加分歧列/索引。详见 memory: go-csharp-shared-postgres。
			client, err := persistence.OpenDB(dsn)
			if err != nil {
				return err
			}
			defer client.Close()

			// ── 单实例锁（独占连接）──
			sqlDB, err := sql.Open("pgx", dsn)
			if err != nil {
				return err
			}
			defer sqlDB.Close()
			g := guard.NewSingleInstanceGuard(sqlDB)

			ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
			defer cancel()

			app := worker.NewApp(log, g)

			// ── 仓储 + 工厂 ──
			exchangeRepo := persistence.NewExchangeRepo(client)
			historyRepo := persistence.NewExchangeOrderHistoryRepo(client)
			orderRepo := persistence.NewOrderRepo(client)
			positionRepo := persistence.NewPositionRepo(client)
			strategyRepo := persistence.NewStrategyRepo(client)
			bindingRepo := persistence.NewStrategyBindingRepo(client)
			factory := exchange.NewFactory()
			provider := trading.NewExchangeClientProvider(factory, encSvc, log)

			// ── 领域事件总线：Redis → tradex:events；否则 Null 降级 ──
			var (
				eventBus trading.DomainEventBus = trading.NullDomainEventBus{}
				rdb      *redis.Client
			)
			if redisAddr != "" {
				rdb = redis.NewClient(&redis.Options{Addr: redisAddr})
				defer rdb.Close()
				eventBus = trading.NewRedisDomainEventBus(rdb)
				log.Info().Str("stream", "tradex:events").Msg("领域事件总线: Redis")
			} else {
				log.Warn().Msg("领域事件总线: 未配置 Redis，降级为 Null（前端实时事件丢失但不阻塞业务）")
			}

			// ── 指标 ──
			metrics, err := trading.NewMetrics()
			if err != nil {
				return err
			}

			settings := trading.DefaultRiskSettings()

			// ExchangeOrderSync（历史订单同步）
			app.AddService(worker.NewExchangeOrderSync(exchangeRepo, historyRepo, encSvc, factory, log))

			// OrderReconciler（订单对账 + 持仓对账）
			fillProj := trading.NewFillProjector(positionRepo, orderRepo, eventBus, log)
			orderRec := trading.NewOrderReconciler(exchangeRepo, orderRepo, provider, eventBus, fillProj, settings, log)
			positionRec := trading.NewPositionReconciler(exchangeRepo, positionRepo, provider, eventBus, metrics, settings, log)
			app.AddService(worker.NewOrderReconcilerService(orderRec, positionRec, settings, log))

			// ── Strategy Evaluator（策略评估引擎）──
			indicatorReg := trading.NewIndicatorRegistry()
			condEval := trading.NewConditionEvaluator()
			decision := trading.NewStrategyDecisionEngine(condEval)
			executor := trading.NewTradeExecutor(exchangeRepo, orderRepo, fillProj, factory, encSvc, settings, log)
			killSwitch := trading.NewKillSwitch(bindingRepo, eventBus, metrics, log)
			riskMgr := trading.NewPortfolioRiskManager(positionRepo, exchangeRepo, killSwitch, factory, settings, log)

			tradeCh := make(chan streaming.TradeEvent, 1000)
			klineCh := make(chan streaming.KlineEvent, 100)

			tradeStream := streaming.NewTradeStreamManager(bindingRepo, exchangeRepo, factory, tradeCh, log)
			klineStream := streaming.NewKlineStreamManager(bindingRepo, exchangeRepo, factory, klineCh, log)

			evaluator := trading.NewStrategyEvaluator(
				bindingRepo, strategyRepo, positionRepo, orderRepo, riskMgr, decision, executor,
				eventBus, metrics, tradeStream, klineStream, tradeCh, klineCh, log, indicatorReg)

			app.AddService(evaluator)

			// ── WorkerCommandSubscriber（Redis Stream 命令订阅）──
			if rdb != nil {
				cmdHandlers := []trading.WorkerCommandHandler{
					trading.NewReconcileNowHandler(orderRec, log),
					trading.NewRefreshSubscriptionsHandler(evaluator, log),
				}
				cmdSub := trading.NewWorkerCommandSubscriber(rdb, cmdHandlers, log)
				app.AddService(cmdSub)
				log.Info().Msg("WorkerCommandSubscriber 已注册")
			} else {
				log.Warn().Msg("未配置 Redis，WorkerCommandSubscriber 跳过")
			}

			log.Info().Msg("TradeX.Worker 已就绪 — 所有服务加载完成")
			if err := app.Run(ctx); err != nil {
				log.Error().Err(err).Msg("Worker 异常退出")
				return err
			}
			log.Info().Msg("TradeX.Worker 已停止")
			return nil
		},
	}

	cmd.Flags().String("database_dsn", "", "postgresql DSN (env: DATABASE_DSN)")
	cmd.Flags().String("redis_addr", "", "redis address (env: REDIS_ADDR)")
	cmd.Flags().String("otlp_endpoint", "", "OTLP HTTP endpoint (env: OTLP_ENDPOINT)")
	cmd.Flags().String("environment", "", "deployment environment (env: ENVIRONMENT)")
	cmd.Flags().String("encryption_key", "", "base64 AES key for exchange secrets (env: ENCRYPTION_KEY)")
	for _, k := range []string{"database_dsn", "redis_addr", "otlp_endpoint", "environment", "encryption_key"} {
		_ = viper.BindPFlag(k, cmd.Flags().Lookup(k))
	}
	viper.AutomaticEnv()

	return cmd
}

func errMissing(key string) error {
	return &missingConfigError{key: key}
}

type missingConfigError struct{ key string }

func (e *missingConfigError) Error() string { return "缺少必需配置: " + e.key }
