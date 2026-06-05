package eventbus

import "context"

type NullEventBus struct{}

func NewNullEventBus() *NullEventBus {
	return &NullEventBus{}
}

func (n *NullEventBus) Publish(_ context.Context, _ string, _ any) error {
	return nil
}

func (n *NullEventBus) Subscribe(_ context.Context, _ string, _ func(ctx context.Context, payload any)) error {
	return nil
}

func (n *NullEventBus) Close() error {
	return nil
}
