package trading

import (
	"encoding/json"
	"strings"
	"testing"
	"time"

	"github.com/google/uuid"

	"tradex/internal/domain"
)

// 验证事件类型全名与 C# 一致（API 端按此派发）。
func TestEventTypeNames(t *testing.T) {
	cases := map[domain.DomainEvent]string{
		OrphanOrderDetectedPayload{}:   "TradeX.Trading.Events.OrphanOrderDetectedPayload",
		PositionDriftDetectedPayload{}: "TradeX.Trading.Events.PositionDriftDetectedPayload",
		PositionUpdatedPayload{}:       "TradeX.Trading.Events.PositionUpdatedPayload",
		OrderPlacedPayload{}:           "TradeX.Trading.Events.OrderPlacedPayload",
		RiskAlertPayload{}:             "TradeX.Trading.Events.RiskAlertPayload",
		KillSwitchActivatedPayload{}:   "TradeX.Trading.Events.KillSwitchActivatedPayload",
		KillSwitchDeactivatedPayload{}: "TradeX.Trading.Events.KillSwitchDeactivatedPayload",
	}
	for evt, want := range cases {
		if got := evt.EventType(); got != want {
			t.Errorf("EventType=%q want %q", got, want)
		}
	}
}

// 验证 payload 序列化为 camelCase 且 decimal 为 JSON 数字（与 C# System.Text.Json 一致）。
func TestPayloadJSONCamelCase(t *testing.T) {
	p := PositionUpdatedPayload{
		PositionID: uuid.New(), TraderID: uuid.New(), ExchangeID: uuid.New(), StrategyID: uuid.New(),
		Pair: "BTCUSDT", Quantity: dn("1.5"), EntryPrice: dn("100"), UnrealizedPnl: dn("0"),
		RealizedPnl: dn("5"), Status: "Closed", UpdatedAt: time.Now().UTC(),
	}
	b, err := json.Marshal(p)
	if err != nil {
		t.Fatal(err)
	}
	s := string(b)
	for _, key := range []string{`"positionId"`, `"traderId"`, `"entryPrice"`, `"unrealizedPnl"`, `"realizedPnl"`, `"updatedAt"`} {
		if !strings.Contains(s, key) {
			t.Errorf("缺少 camelCase 字段 %s in %s", key, s)
		}
	}
	// decimal 作为 JSON 数字（无引号）
	if !strings.Contains(s, `"quantity":1.5`) {
		t.Errorf("decimal 应为 JSON 数字: %s", s)
	}
}
