package cli

import (
	"context"
	"database/sql"
	"os"
	"os/signal"
	"syscall"
	"time"

	_ "github.com/jackc/pgx/v5/stdlib"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"

	"tradex/internal/infrastructure/analysis"
	"tradex/internal/infrastructure/eventbus"
	"tradex/internal/infrastructure/exchange"
	"tradex/internal/domain/indicator"
	"tradex/internal/infrastructure/worker"
	"tradex/internal/infrastructure/persistence"
	"tradex/internal/infrastructure/telemetry"
)

func NewWorkerCmd() *cobra.Command {
	cmd := &cobra.Command{
		Use:   "worker",
		Short: "TradeX Backtest Worker",
		Run: func(_ *cobra.Command, _ []string) {
			log := zerolog.New(os.Stdout).With().Timestamp().Logger()

			maxConcurrency := viper.GetInt("max_concurrency")
			taskTimeout := viper.GetInt("task_timeout_minutes")
			dsn := viper.GetString("database_dsn")
			otlpEndpoint := viper.GetString("otlp_endpoint")
			redisAddr := viper.GetString("redis_addr")

			shutdown, err := telemetry.InitOTel(context.Background(), telemetry.Config{
				ServiceName:    "tradex-worker",
				ServiceVersion: "0.1.0",
				OTLPEndpoint:   otlpEndpoint,
				Environment:    viper.GetString("environment"),
			})
			if err != nil {
				log.Warn().Err(err).Msg("otel init failed, continuing without tracing")
			} else {
				defer shutdown(context.Background())
			}

			client, err := persistence.OpenDB(dsn)
			if err != nil {
				log.Fatal().Err(err).Msg("failed to open database")
			}
			defer client.Close()

			if err := persistence.AutoMigrate(context.Background(), client); err != nil {
				log.Fatal().Err(err).Msg("failed to run migrations")
			}
			log.Info().Msg("database migrations applied")

			ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
			defer cancel()

			// PG advisory lock for single-instance guard
			var guard *worker.BacktestWorkerGuard
			if dsn != "" {
				if sqlDB, err := sql.Open("pgx", dsn); err == nil {
					guard = worker.NewBacktestWorkerGuard(sqlDB)
					defer sqlDB.Close()
				}
			}

			rmCfg := worker.ResourceMonitorConfig{
				MonitorIntervalSec: viper.GetInt("monitor_interval_sec"),
				CpuWarningPercent:  viper.GetInt("cpu_warning_pct"),
				CpuCriticalPercent: viper.GetInt("cpu_critical_pct"),
				CpuAbsolutePercent: viper.GetInt("cpu_absolute_pct"),
			}
			// set defaults if zero
			def := worker.DefaultResourceMonitorConfig()
			if rmCfg.MonitorIntervalSec == 0 {
				rmCfg.MonitorIntervalSec = def.MonitorIntervalSec
			}
			if rmCfg.CpuWarningPercent == 0 {
				rmCfg.CpuWarningPercent = def.CpuWarningPercent
			}
			if rmCfg.CpuCriticalPercent == 0 {
				rmCfg.CpuCriticalPercent = def.CpuCriticalPercent
			}
			if rmCfg.CpuAbsolutePercent == 0 {
				rmCfg.CpuAbsolutePercent = def.CpuAbsolutePercent
			}

			btRepo := persistence.NewBacktestRepo(client)
			taskQueue := worker.NewTaskQueue(maxConcurrency * 2)
			resMon := worker.NewResourceMonitor(ctx, maxConcurrency, rmCfg)
			tracker := worker.NewRunningBacktestTracker()
			analysisStore := analysis.NewStore()

			klineCache := persistence.NewKlineCache(10 * time.Minute)
			klineClient := exchange.NewBinanceClient()

			reg := indicator.NewRegistry()
			reg.Register(indicator.NewSMA(20))
			reg.Register(indicator.NewSMA(50))
			reg.Register(indicator.NewEMA(20))
			reg.Register(indicator.NewRSI(14))
			reg.Register(indicator.NewMACD(12, 26, 9))
			reg.Register(indicator.NewBollingerBands(20, 2))
			reg.Register(indicator.NewStochastic(5, 3))

			sch := worker.NewBacktestScheduler(btRepo, taskQueue, resMon, reg, klineCache, klineClient, tracker, analysisStore, log)

			var redisBus *eventbus.RedisEventBus
			if redisAddr != "" {
				rdb := redis.NewClient(&redis.Options{Addr: redisAddr})
				defer rdb.Close()

				redisBus = eventbus.NewRedisEventBus(rdb)

				listener := worker.NewTaskListener(btRepo, taskQueue, redisBus, log)
				listener.Start(ctx)

				cancelConsumer := worker.NewCancellationConsumer(tracker, redisBus, log)
				cancelConsumer.Start(ctx)

				log.Info().Str("redis_addr", redisAddr).Msg("redis stream listeners started")
			} else {
				log.Warn().Msg("redis not configured, tasks must be polled from db")
			}

			feeRate := viper.GetFloat64("fee_rate")

			go sch.Run(ctx, worker.SchedulerConfig{
				MaxConcurrency:     maxConcurrency,
				TaskTimeoutMinutes: taskTimeout,
				FeeRate:            feeRate,
			}, guard)

			log.Info().
				Int("max_concurrency", maxConcurrency).
				Int("task_timeout_minutes", taskTimeout).
				Msg("worker started")

			<-ctx.Done()
			log.Info().Msg("worker shutting down")
		},
	}

	cmd.Flags().Int("max_concurrency", 2, "max concurrent backtest tasks (env: MAX_CONCURRENCY)")
	cmd.Flags().Int("task_timeout_minutes", 30, "task timeout in minutes (env: TASK_TIMEOUT_MINUTES)")
	cmd.Flags().Float64("fee_rate", 0.001, "fee rate per side (env: FEE_RATE)")
	cmd.Flags().Int("monitor_interval_sec", 5, "resource monitor interval (env: MONITOR_INTERVAL_SEC)")
	cmd.Flags().Int("cpu_warning_pct", 50, "CPU warning percent (env: CPU_WARNING_PCT)")
	cmd.Flags().Int("cpu_critical_pct", 75, "CPU critical percent (env: CPU_CRITICAL_PCT)")
	cmd.Flags().Int("cpu_absolute_pct", 90, "CPU absolute max percent (env: CPU_ABSOLUTE_PCT)")
	cmd.Flags().String("database_dsn", "", "postgresql DSN (env: DATABASE_DSN)")
	cmd.Flags().String("redis_addr", "", "redis address (env: REDIS_ADDR)")
	cmd.Flags().String("otlp_endpoint", "", "OTLP HTTP endpoint (env: OTLP_ENDPOINT)")
	cmd.Flags().String("environment", "", "deployment environment (env: ENVIRONMENT)")
	viper.BindPFlag("max_concurrency", cmd.Flags().Lookup("max_concurrency"))
	viper.BindPFlag("task_timeout_minutes", cmd.Flags().Lookup("task_timeout_minutes"))
	viper.BindPFlag("fee_rate", cmd.Flags().Lookup("fee_rate"))
	viper.BindPFlag("monitor_interval_sec", cmd.Flags().Lookup("monitor_interval_sec"))
	viper.BindPFlag("cpu_warning_pct", cmd.Flags().Lookup("cpu_warning_pct"))
	viper.BindPFlag("cpu_critical_pct", cmd.Flags().Lookup("cpu_critical_pct"))
	viper.BindPFlag("cpu_absolute_pct", cmd.Flags().Lookup("cpu_absolute_pct"))
	viper.BindPFlag("database_dsn", cmd.Flags().Lookup("database_dsn"))
	viper.BindPFlag("redis_addr", cmd.Flags().Lookup("redis_addr"))
	viper.BindPFlag("otlp_endpoint", cmd.Flags().Lookup("otlp_endpoint"))
	viper.BindPFlag("environment", cmd.Flags().Lookup("environment"))
	viper.AutomaticEnv()

	return cmd
}
