package worker

import (
	"context"
	"errors"
	"fmt"
	"time"

	"github.com/rs/zerolog"
	"golang.org/x/sync/errgroup"

	guard "tradex/internal/infra/guard"
)

// Service 是一个长生命周期后台服务，对应 C# 的 BackgroundService/IHostedService。
// Run 应在 ctx 取消时尽快返回；正常停机返回 nil 或 context.Canceled。
type Service interface {
	Name() string
	Run(ctx context.Context) error
}

// App 装配并驱动实盘 Worker 的全部后台服务，统一生命周期与优雅停机。
type App struct {
	log      zerolog.Logger
	guard    guard.Guard
	services []Service
}

// NewApp 构造一个 App。guard 可为 nil（不启用单实例锁，仅用于测试）。
func NewApp(log zerolog.Logger, g guard.Guard) *App {
	return &App{log: log, guard: g}
}

// AddService 按注册顺序追加一个后台服务（顺序即启动顺序）。
func (a *App) AddService(svc Service) { a.services = append(a.services, svc) }

// Run 获取单实例锁、并发启动所有服务，并在 ctx 取消时优雅停机、释放锁。
func (a *App) Run(ctx context.Context) error {
	if a.guard != nil {
		if err := a.guard.TryAcquire(ctx); err != nil {
			return err
		}
		a.log.Info().Str("lock_id", fmt.Sprintf("%#x", guard.LiveWorkerLockID)).Msg("Worker 单实例锁获取成功")
		defer a.releaseGuard()
	}

	g, gctx := errgroup.WithContext(ctx)

	// 哨兵：保证进程在收到停机信号前一直存活，即便暂无（或全部为短生命周期）后台服务。
	// 对应 C# Host.RunAsync 在无显式服务时仍阻塞到关闭信号的语义。
	g.Go(func() error {
		<-gctx.Done()
		return nil
	})

	for _, svc := range a.services {
		svc := svc
		g.Go(func() error {
			a.log.Info().Str("service", svc.Name()).Msg("后台服务启动")
			err := svc.Run(gctx)
			if err != nil && !errors.Is(err, context.Canceled) {
				return fmt.Errorf("%s: %w", svc.Name(), err)
			}
			a.log.Info().Str("service", svc.Name()).Msg("后台服务已停止")
			return nil
		})
	}

	err := g.Wait()
	if err != nil && !errors.Is(err, context.Canceled) {
		return err
	}
	return nil
}

func (a *App) releaseGuard() {
	relCtx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	if err := a.guard.Release(relCtx); err != nil {
		a.log.Error().Err(err).Msg("释放单实例锁失败")
		return
	}
	a.log.Info().Str("lock_id", fmt.Sprintf("%#x", guard.LiveWorkerLockID)).Msg("Worker 单实例锁已释放")
}
