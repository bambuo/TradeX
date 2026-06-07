package trading

import (
	"context"
	"sync"
	"time"

	"github.com/google/uuid"
	"github.com/rs/zerolog"

	"tradex/internal/domain"
)

type KillSwitch struct {
	mu         sync.RWMutex
	active     bool
	lastReason string
	lastAt     time.Time

	bindingRepo domain.StrategyBindingRepository
	eventBus    DomainEventBus
	metrics     *Metrics
	log         zerolog.Logger
}

func NewKillSwitch(
	bindingRepo domain.StrategyBindingRepository,
	eventBus DomainEventBus,
	metrics *Metrics,
	log zerolog.Logger,
) *KillSwitch {
	return &KillSwitch{
		bindingRepo: bindingRepo,
		eventBus:    eventBus,
		metrics:     metrics,
		log:         log.With().Str("component", "KillSwitch").Logger(),
	}
}

func (k *KillSwitch) IsActive() bool {
	k.mu.RLock()
	defer k.mu.RUnlock()
	return k.active
}

func (k *KillSwitch) LastReason() string {
	k.mu.RLock()
	defer k.mu.RUnlock()
	return k.lastReason
}

func (k *KillSwitch) LastActivatedAt() *time.Time {
	k.mu.RLock()
	defer k.mu.RUnlock()
	if k.lastAt.IsZero() {
		return nil
	}
	return &k.lastAt
}

func (k *KillSwitch) Activate(ctx context.Context, reason string, actorID *uuid.UUID) error {
	k.mu.Lock()
	if k.active {
		k.mu.Unlock()
		k.log.Info().Msg("Kill Switch 已激活, 跳过重复激活")
		return nil
	}
	k.active = true
	k.lastReason = reason
	now := time.Now().UTC()
	k.lastAt = now
	k.mu.Unlock()

	actives, err := k.bindingRepo.GetAllActive(ctx)
	if err != nil {
		return err
	}
	for _, b := range actives {
		b.Disable()
	}
	if err := k.bindingRepo.UpdateRange(ctx, actives); err != nil {
		return err
	}

	if err := k.eventBus.Publish(ctx, KillSwitchActivatedPayload{
		Reason:               reason,
		ActorUserID:          actorID,
		ActivatedAtUtc:       now,
		DisabledBindingCount: len(actives),
	}); err != nil {
		k.log.Error().Err(err).Msg("发布 Kill Switch 激活事件失败")
	}

	k.log.Warn().Str("reason", reason).Int("disabled_bindings", len(actives)).
		Msg("Kill Switch 已激活")
	return nil
}

func (k *KillSwitch) Deactivate(ctx context.Context, reason string, actorID *uuid.UUID) error {
	k.mu.Lock()
	if !k.active {
		k.mu.Unlock()
		k.log.Info().Msg("Kill Switch 未激活, 跳过解除")
		return nil
	}
	k.active = false
	k.lastReason = "已解除: " + reason
	k.mu.Unlock()

	if err := k.eventBus.Publish(ctx, KillSwitchDeactivatedPayload{
		Reason:           reason,
		ActorUserID:      actorID,
		DeactivatedAtUtc: time.Now().UTC(),
	}); err != nil {
		k.log.Error().Err(err).Msg("发布 Kill Switch 解除事件失败")
	}

	k.log.Warn().Str("reason", reason).Msg("Kill Switch 已解除")
	return nil
}
