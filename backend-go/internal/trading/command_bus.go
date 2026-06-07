package trading

import (
	"context"
	"encoding/json"
	"fmt"
	"strings"
	"time"

	"github.com/google/uuid"
	"github.com/redis/go-redis/v9"
	"github.com/rs/zerolog"
)

const (
	cmdStreamKey     = "tradex:cmd"
	cmdConsumerGroup = "worker-cmd"
	cmdPollDelay     = 500 * time.Millisecond
	cmdStaleIdle     = 60 * time.Second
	cmdClaimInterval = 30 * time.Second
	cmdMaxLen        = 10000
)

type WorkerCommand struct {
	Type     string `json:"type"`
	ArgsJSON string `json:"argsJson"`
}

type WorkerCommandHandler interface {
	CommandType() string
	Handle(ctx context.Context, argsJSON string) error
}

type ReconcileNowHandler struct {
	reconciler *OrderReconciler
	log        zerolog.Logger
}

func NewReconcileNowHandler(reconciler *OrderReconciler, log zerolog.Logger) *ReconcileNowHandler {
	return &ReconcileNowHandler{reconciler: reconciler, log: log}
}

func (h *ReconcileNowHandler) CommandType() string { return "ReconcileNow" }

func (h *ReconcileNowHandler) Handle(ctx context.Context, _ string) error {
	h.log.Info().Msg("收到 ReconcileNow 命令")
	return h.reconciler.Reconcile(ctx)
}

type RefreshSubscriptionsHandler struct {
	evaluator *StrategyEvaluator
	log       zerolog.Logger
}

func NewRefreshSubscriptionsHandler(evaluator *StrategyEvaluator, log zerolog.Logger) *RefreshSubscriptionsHandler {
	return &RefreshSubscriptionsHandler{evaluator: evaluator, log: log}
}

func (h *RefreshSubscriptionsHandler) CommandType() string { return "RefreshSubscriptions" }

func (h *RefreshSubscriptionsHandler) Handle(ctx context.Context, _ string) error {
	h.log.Info().Msg("收到 RefreshSubscriptions 命令")
	return h.evaluator.Refresh(ctx)
}

type WorkerCommandSubscriber struct {
	rdb      *redis.Client
	handlers map[string]WorkerCommandHandler
	log      zerolog.Logger
}

func NewWorkerCommandSubscriber(rdb *redis.Client, handlers []WorkerCommandHandler, log zerolog.Logger) *WorkerCommandSubscriber {
	hm := make(map[string]WorkerCommandHandler, len(handlers))
	for _, h := range handlers {
		hm[h.CommandType()] = h
	}
	return &WorkerCommandSubscriber{
		rdb:      rdb,
		handlers: hm,
		log:      log.With().Str("component", "WorkerCommandSubscriber").Logger(),
	}
}

func (s *WorkerCommandSubscriber) Name() string { return "WorkerCommandSubscriber" }

func (s *WorkerCommandSubscriber) Run(ctx context.Context) error {
	if len(s.handlers) == 0 {
		s.log.Warn().Msg("未发现命令 handler，WorkerCommandSubscriber 不会启动")
		return nil
	}

	consumer := fmt.Sprintf("worker-%s", uuid.New().String()[:8])
	if err := s.ensureGroup(ctx); err != nil {
		return err
	}
	s.log.Info().Str("stream", cmdStreamKey).Str("group", cmdConsumerGroup).Str("consumer", consumer).
		Strs("handlers", keys(s.handlers)).Msg("WorkerCommandSubscriber 启动")

	s.drainPending(ctx, consumer)

	lastClaim := time.Now()
	for ctx.Err() == nil {
		if time.Since(lastClaim) > cmdClaimInterval {
			lastClaim = time.Now()
			s.claimStale(ctx, consumer)
		}

		entries, err := s.rdb.XReadGroup(ctx, &redis.XReadGroupArgs{
			Group:    cmdConsumerGroup,
			Consumer: consumer,
			Streams:  []string{cmdStreamKey, ">"},
			Count:    20,
			Block:    0,
		}).Result()
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			s.log.Error().Err(err).Msg("XREADGROUP 异常，1s 后重试")
			sleep(ctx, time.Second)
			continue
		}
		for _, stream := range entries {
			for _, msg := range stream.Messages {
				if ctx.Err() != nil {
					return nil
				}
				s.processMessage(ctx, msg)
			}
		}
	}
	return nil
}

func (s *WorkerCommandSubscriber) ensureGroup(ctx context.Context) error {
	err := s.rdb.XGroupCreateMkStream(ctx, cmdStreamKey, cmdConsumerGroup, "0").Err()
	if err != nil && !strings.Contains(err.Error(), "BUSYGROUP") {
		return err
	}
	return nil
}

func (s *WorkerCommandSubscriber) drainPending(ctx context.Context, consumer string) {
	entries, err := s.rdb.XReadGroup(ctx, &redis.XReadGroupArgs{
		Group:    cmdConsumerGroup,
		Consumer: consumer,
		Streams:  []string{cmdStreamKey, "0"},
		Count:    100,
	}).Result()
	if err != nil {
		s.log.Warn().Err(err).Msg("PEL 清理读取失败")
		return
	}
	for _, stream := range entries {
		for _, msg := range stream.Messages {
			s.processMessage(ctx, msg)
		}
	}
}

func (s *WorkerCommandSubscriber) claimStale(ctx context.Context, consumer string) {
	entries, _, err := s.rdb.XAutoClaim(ctx, &redis.XAutoClaimArgs{
		Stream:   cmdStreamKey,
		Group:    cmdConsumerGroup,
		Consumer: consumer,
		MinIdle:  cmdStaleIdle,
		Count:    20,
	}).Result()
	if err != nil {
		return
	}
	for _, msg := range entries {
		s.processMessage(ctx, msg)
	}
}

func (s *WorkerCommandSubscriber) processMessage(ctx context.Context, msg redis.XMessage) {
	rawPayload, ok := msg.Values["payload"]
	if !ok {
		s.rdb.XAck(ctx, cmdStreamKey, cmdConsumerGroup, msg.ID)
		return
	}
	payloadStr, ok := rawPayload.(string)
	if !ok {
		s.rdb.XAck(ctx, cmdStreamKey, cmdConsumerGroup, msg.ID)
		return
	}
	var cmd WorkerCommand
	if err := json.Unmarshal([]byte(payloadStr), &cmd); err != nil {
		s.log.Warn().Err(err).Str("id", msg.ID).Msg("解析命令失败，ACK 跳过")
		s.rdb.XAck(ctx, cmdStreamKey, cmdConsumerGroup, msg.ID)
		return
	}
	handler, ok := s.handlers[cmd.Type]
	if !ok {
		s.log.Warn().Str("type", cmd.Type).Msg("未知命令类型，ACK 跳过")
		s.rdb.XAck(ctx, cmdStreamKey, cmdConsumerGroup, msg.ID)
		return
	}
	if err := handler.Handle(ctx, cmd.ArgsJSON); err != nil {
		s.log.Error().Err(err).Str("type", cmd.Type).Str("id", msg.ID).Msg("命令处理失败，留待重试")
		return
	}
	s.rdb.XAck(ctx, cmdStreamKey, cmdConsumerGroup, msg.ID)
}

func keys[K comparable, V any](m map[K]V) []K {
	ks := make([]K, 0, len(m))
	for k := range m {
		ks = append(ks, k)
	}
	return ks
}

func sleep(ctx context.Context, d time.Duration) {
	t := time.NewTimer(d)
	defer t.Stop()
	select {
	case <-ctx.Done():
	case <-t.C:
	}
}
