package guard

import "context"

type Guard interface {
	TryAcquire(ctx context.Context) error
	Release(ctx context.Context) error
}
