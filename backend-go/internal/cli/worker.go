package cli

import (
	"context"
	"database/sql"
	"os"
	"os/signal"
	"strings"
	"sync"
	"syscall"
	"time"

	_ "github.com/jackc/pgx/v5/stdlib"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"

	"tradex/internal/domain/indicator"
	"tradex/internal/infra/analysis"
	"tradex/internal/infra/eventbus"
	"tradex/internal/infra/exchange"
	"tradex/internal/infra/persistence"
	"tradex/internal/infra/telemetry"
	"tradex/internal/infra/worker"
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

			log.Info().Str("dsn", maskDSN(dsn)).Bool("redis", redisAddr != "").Msg("Worker 启动")

			shutdown, err := telemetry.InitOTel(context.Background(), telemetry.Config{
				ServiceName:    "tradex-worker",
				ServiceVersion: "0.1.0",
				OTLPEndpoint:   otlpEndpoint,
				Environment:    viper.GetString("environment"),
			})
			if err != nil {
				log.Warn().Err(err).Msg("遥测初始化失败，无追踪继续运行")
			} else {
				defer shutdown(context.Background())
			}

			client, err := persistence.OpenDB(dsn)
			if err != nil {
				log.Fatal().Err(err).Msg("打开数据库失败")
			}
			defer client.Close()

			if err := persistence.AutoMigrate(context.Background(), client); err != nil {
				log.Fatal().Err(err).Msg("运行数据库迁移失败")
			}
			log.Info().Msg("数据库迁移已应用")

			ctx, cancel := signal.NotifyContext(context.Background(), syscall.SIGINT, syscall.SIGTERM)
			defer cancel()
			var wg sync.WaitGroup

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
				wg.Add(1)
				go func() {
					defer wg.Done()
					listener.Start(ctx)
				}()

				cancelConsumer := worker.NewCancellationConsumer(tracker, redisBus, log)
				wg.Add(1)
				go func() {
					defer wg.Done()
					cancelConsumer.Start(ctx)
				}()

				log.Info().Str("redis_addr", redisAddr).Msg("Redis 流监听器已启动")
			} else {
				log.Warn().Msg("Redis 未配置，任务将通过 DB 轮询")
			}

			feeRate := viper.GetFloat64("fee_rate")

			wg.Add(1)
			go func() {
				defer wg.Done()
				sch.Run(ctx, worker.SchedulerConfig{
					MaxConcurrency:     maxConcurrency,
					TaskTimeoutMinutes: taskTimeout,
					FeeRate:            feeRate,
				}, guard)
			}()

			log.Info().
				Int("max_concurrency", maxConcurrency).
				Int("task_timeout_minutes", taskTimeout).
				Msg("Worker 已启动")

			<-ctx.Done()
			log.Info().Msg("Worker 关闭，等待 goroutine 结束...")
			wg.Wait()
			log.Info().Msg("Worker 已停止")
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

func maskDSN(dsn string) string {
	if dsn == "" {
		return ""
	}
	// postgres://user:pass@host:port/db → postgres://user:***@host:port/db
	afterScheme := strings.SplitN(dsn, "://", 2)
	if len(afterScheme) < 2 {
		return dsn
	}
	rest := afterScheme[1]
	atIdx := strings.LastIndex(rest, "@")
	colonIdx := strings.Index(rest, ":")
	if colonIdx > 0 && atIdx > colonIdx {
		rest = rest[:colonIdx+1] + "***" + rest[atIdx:]
	}
	return afterScheme[0] + "://" + rest
}
