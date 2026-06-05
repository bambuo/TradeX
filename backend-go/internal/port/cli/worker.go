package cli

import (
	"context"
	"os"
	"os/signal"
	"syscall"
	"time"

	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"

	"github.com/tradex/backend-go/internal/infra/eventbus"
	"github.com/tradex/backend-go/internal/infra/exchange"
	"github.com/tradex/backend-go/internal/domain/indicator"
	"github.com/tradex/backend-go/internal/infra/scheduler"
	"github.com/tradex/backend-go/internal/infra/persistence"
	"github.com/tradex/backend-go/internal/infra/telemetry"
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

			client, err := storage.OpenDB(dsn)
			if err != nil {
				log.Fatal().Err(err).Msg("failed to open database")
			}
			defer client.Close()

			if err := storage.AutoMigrate(context.Background(), client); err != nil {
				log.Fatal().Err(err).Msg("failed to run migrations")
			}
			log.Info().Msg("database migrations applied")

			ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
			defer cancel()

			repo := storage.NewBacktestRepo(client)
			taskQueue := scheduler.NewTaskQueue(maxConcurrency * 2)
			resMon := scheduler.NewResourceMonitor(ctx, maxConcurrency)
			tracker := scheduler.NewRunningBacktestTracker()

			klineCache := storage.NewKlineCache(10 * time.Minute)
			klineClient := exchange.NewBinanceClient()

			reg := indicator.NewRegistry()
			reg.Register(indicator.NewSMA(20))
			reg.Register(indicator.NewSMA(50))
			reg.Register(indicator.NewEMA(20))
			reg.Register(indicator.NewRSI(14))
			reg.Register(indicator.NewMACD(12, 26, 9))
			reg.Register(indicator.NewBollingerBands(20, 2))

			sch := scheduler.NewBacktestScheduler(repo, taskQueue, resMon, reg, klineCache, klineClient, tracker, log)

			var redisBus *eventbus.RedisEventBus
			if redisAddr != "" {
				rdb := redis.NewClient(&redis.Options{Addr: redisAddr})
				defer rdb.Close()

				redisBus = eventbus.NewRedisEventBus(rdb)

				listener := scheduler.NewTaskListener(repo, taskQueue, redisBus, log)
				listener.Start(ctx)

				cancelConsumer := scheduler.NewCancellationConsumer(tracker, redisBus, log)
				cancelConsumer.Start(ctx)

				log.Info().Str("redis_addr", redisAddr).Msg("redis stream listeners started")
			} else {
				log.Warn().Msg("redis not configured, tasks must be polled from db")
			}

			go sch.Run(ctx, scheduler.SchedulerConfig{
				MaxConcurrency:     maxConcurrency,
				TaskTimeoutMinutes: taskTimeout,
			})

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
	cmd.Flags().String("database_dsn", "", "postgresql DSN (env: DATABASE_DSN)")
	cmd.Flags().String("redis_addr", "", "redis address (env: REDIS_ADDR)")
	cmd.Flags().String("otlp_endpoint", "", "OTLP HTTP endpoint (env: OTLP_ENDPOINT)")
	cmd.Flags().String("environment", "", "deployment environment (env: ENVIRONMENT)")
	viper.BindPFlag("max_concurrency", cmd.Flags().Lookup("max_concurrency"))
	viper.BindPFlag("task_timeout_minutes", cmd.Flags().Lookup("task_timeout_minutes"))
	viper.BindPFlag("database_dsn", cmd.Flags().Lookup("database_dsn"))
	viper.BindPFlag("redis_addr", cmd.Flags().Lookup("redis_addr"))
	viper.BindPFlag("otlp_endpoint", cmd.Flags().Lookup("otlp_endpoint"))
	viper.BindPFlag("environment", cmd.Flags().Lookup("environment"))
	viper.AutomaticEnv()

	return cmd
}
