package cli

import (
	"context"
	"net/http"
	_ "net/http/pprof"
	"os"

	"github.com/gin-gonic/gin"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"
	"github.com/spf13/cobra"
	"github.com/spf13/viper"
	"go.opentelemetry.io/contrib/instrumentation/github.com/gin-gonic/gin/otelgin"

	"tradex/internal/infra/eventbus"
	"tradex/internal/infra/persistence"
	"tradex/internal/infra/telemetry"
	"tradex/internal/server/api/handler"
	"tradex/internal/server/app"
)

func NewAPICmd() *cobra.Command {
	cmd := &cobra.Command{
		Use:   "api",
		Short: "TradeX Backtest API Server",
		Run: func(_ *cobra.Command, _ []string) {
			log := zerolog.New(os.Stdout).With().Timestamp().Logger()

			dsn := viper.GetString("database_dsn")
			otlpEndpoint := viper.GetString("otlp_endpoint")

			shutdown, err := telemetry.InitOTel(context.Background(), telemetry.Config{
				ServiceName:    "tradex-api",
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

			btRepo := persistence.NewBacktestRepo(client)
			svc := app.NewBacktestService(btRepo)

			redisAddr := viper.GetString("redis_addr")
			var redisBus *eventbus.RedisEventBus
			if redisAddr != "" {
				rdb := redis.NewClient(&redis.Options{Addr: redisAddr})
				defer rdb.Close()
				redisBus = eventbus.NewRedisEventBus(rdb)
			}

			h := handler.NewBacktestHandler(svc, log)
			if redisBus != nil {
				h.WithCancelPublisher(eventbus.NewRedisCancelNotifier(redisBus))
			}

			r := gin.New()
			r.Use(handler.RecoveryMiddleware(log))
			r.Use(handler.ZerologMiddleware(log))
			r.Use(handler.AuthMiddleware())
			r.Use(handler.RateLimitMiddleware())
			r.Use(otelgin.Middleware("tradex-api"))
			r.GET("/debug/pprof/*pprof", gin.WrapH(http.DefaultServeMux))
			r.GET("/debug/pprof", gin.WrapH(http.DefaultServeMux))
			h.RegisterRoutes(r)

			addr := viper.GetString("listen")
			log.Info().Str("addr", addr).Msg("API server starting")
			if err := r.Run(addr); err != nil {
				log.Fatal().Err(err).Msg("server failed")
			}
		},
	}

	cmd.Flags().String("listen", "", "listen address (env: LISTEN)")
	cmd.Flags().String("database_dsn", "", "postgresql DSN (env: DATABASE_DSN)")
	cmd.Flags().String("redis_addr", "", "redis address (env: REDIS_ADDR)")
	cmd.Flags().String("otlp_endpoint", "", "OTLP HTTP endpoint (env: OTLP_ENDPOINT)")
	cmd.Flags().String("environment", "", "deployment environment (env: ENVIRONMENT)")
	viper.BindPFlag("listen", cmd.Flags().Lookup("listen"))
	viper.BindPFlag("database_dsn", cmd.Flags().Lookup("database_dsn"))
	viper.BindPFlag("redis_addr", cmd.Flags().Lookup("redis_addr"))
	viper.BindPFlag("otlp_endpoint", cmd.Flags().Lookup("otlp_endpoint"))
	viper.BindPFlag("environment", cmd.Flags().Lookup("environment"))
	viper.AutomaticEnv()

	return cmd
}
