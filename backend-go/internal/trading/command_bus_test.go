package trading

import (
	"context"
	"testing"

	"github.com/rs/zerolog"
)

type recordingHandler struct {
	typ    string
	called int
	last   string
}

func (h *recordingHandler) CommandType() string { return h.typ }
func (h *recordingHandler) Handle(_ context.Context, argsJSON string) error {
	h.called++
	h.last = argsJSON
	return nil
}

func TestWorkerCommandSubscriber_DispatchByType(t *testing.T) {
	h1 := &recordingHandler{typ: "ReconcileNow"}
	h2 := &recordingHandler{typ: "RefreshSubscriptions"}
	sub := NewWorkerCommandSubscriber(nil, []WorkerCommandHandler{h1, h2}, zerolog.Nop())

	if len(sub.handlers) != 2 {
		t.Fatalf("应有 2 个 handler，实际 %d", len(sub.handlers))
	}
	if _, ok := sub.handlers["ReconcileNow"]; !ok {
		t.Error("缺少 ReconcileNow handler")
	}
	if _, ok := sub.handlers["RefreshSubscriptions"]; !ok {
		t.Error("缺少 RefreshSubscriptions handler")
	}
}

func TestReconcileNowHandler_CommandType(t *testing.T) {
	handler := NewReconcileNowHandler(nil, zerolog.Nop())
	if handler.CommandType() != "ReconcileNow" {
		t.Errorf("CommandType = %q, want ReconcileNow", handler.CommandType())
	}
}

func TestRefreshSubscriptionsHandler_CommandType(t *testing.T) {
	handler := NewRefreshSubscriptionsHandler(nil, zerolog.Nop())
	if handler.CommandType() != "RefreshSubscriptions" {
		t.Errorf("CommandType = %q, want RefreshSubscriptions", handler.CommandType())
	}
}
