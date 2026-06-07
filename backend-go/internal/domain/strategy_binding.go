package domain

import (
	"strings"
	"time"

	"github.com/google/uuid"
)

// StrategyBinding 策略绑定：把一个策略模板部署到 (trader, exchange, pairs, timeframe)。
// 对应 C# TradeX.Core.Models.StrategyBinding。
//
// 注意 Pairs 在业务上是逗号分隔的交易对字符串（StrategyEvaluationConsumer 以 ',' 切分），
// 即便其 C# 默认值字面量为 "[]"。PairList 统一切分逻辑。
type StrategyBinding struct {
	ID         uuid.UUID     `json:"id"`
	StrategyID uuid.UUID     `json:"strategyId"`
	Name       string        `json:"name"`
	TraderID   uuid.UUID     `json:"traderId"`
	ExchangeID uuid.UUID     `json:"exchangeId"`
	Pairs      string        `json:"pairs"`
	Timeframe  string        `json:"timeframe"`
	Status     BindingStatus `json:"status"`
	CreatedBy  uuid.UUID     `json:"createdBy"`
	CreatedAt  time.Time     `json:"createdAt"`
	UpdatedAt  time.Time     `json:"updatedAt"`
}

// IsActive 报告绑定是否处于激活态（行情驱动评估只看 Active）。
func (b *StrategyBinding) IsActive() bool { return b.Status == BindingStatusActive }

func (b *StrategyBinding) Disable() { b.Status = BindingStatusDisabled }

func (b *StrategyBinding) Enable() { b.Status = BindingStatusActive }

// PairList 把逗号分隔的 Pairs 切成去空白、去空项的交易对列表。
func (b *StrategyBinding) PairList() []string {
	parts := strings.Split(b.Pairs, ",")
	out := make([]string, 0, len(parts))
	for _, p := range parts {
		if p = strings.TrimSpace(p); p != "" {
			out = append(out, p)
		}
	}
	return out
}

// EffectiveTimeframe 返回有效的 K 线周期，空则回退到 "15m"（与 KlineStreamManager 一致）。
func (b *StrategyBinding) EffectiveTimeframe() string {
	if strings.TrimSpace(b.Timeframe) == "" {
		return "15m"
	}
	return b.Timeframe
}
