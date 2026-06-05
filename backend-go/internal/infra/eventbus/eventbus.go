package eventbus

import "context"

type EventBus interface {
	Publish(ctx context.Context, channel string, payload any) error
	Subscribe(ctx context.Context, channel string, handler func(ctx context.Context, payload any)) error
	Close() error
}
