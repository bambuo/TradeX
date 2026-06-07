package trading

import (
	"encoding/json"
	"fmt"

	"github.com/shopspring/decimal"
)

// VolatilityGridExecutionRule 波动率网格执行规则。对应 C# VolatilityGridExecutionRule。
type VolatilityGridExecutionRule struct {
	Type                   string
	EntryVolatilityPercent decimal.Decimal
	RebalancePercent       decimal.Decimal
	BasePositionSize       decimal.Decimal
	MaxPositionSize        decimal.Decimal
	MaxPyramidingLevels    int
	NoStopLoss             bool
	SlippageTolerance      decimal.Decimal
	MaxDailyLoss           decimal.Decimal
}

// DefaultVolatilityGridRule 与 C# VolatilityGridExecutionRule.Default 一致。
func DefaultVolatilityGridRule() VolatilityGridExecutionRule {
	return VolatilityGridExecutionRule{
		Type:                   "volatility_grid",
		EntryVolatilityPercent: decimal.NewFromInt(1),
		RebalancePercent:       decimal.NewFromInt(1),
		BasePositionSize:       decimal.NewFromInt(100),
		MaxPositionSize:        decimal.NewFromInt(500),
		MaxPyramidingLevels:    5,
		NoStopLoss:             true,
		SlippageTolerance:      decimal.NewFromFloat(0.0005),
		MaxDailyLoss:           decimal.NewFromInt(200),
	}
}

type volatilityGridDTO struct {
	Type                   string          `json:"type"`
	EntryVolatilityPercent decimal.Decimal `json:"entryVolatilityPercent"`
	RebalancePercent       decimal.Decimal `json:"rebalancePercent"`
	BasePositionSize       decimal.Decimal `json:"basePositionSize"`
	MaxPositionSize        decimal.Decimal `json:"maxPositionSize"`
	MaxPyramidingLevels    int             `json:"maxPyramidingLevels"`
	NoStopLoss             bool            `json:"noStopLoss"`
	SlippageTolerance      decimal.Decimal `json:"slippageTolerance"`
	MaxDailyLoss           decimal.Decimal `json:"maxDailyLoss"`
}

// TryParseVolatilityGridRule 解析执行规则 JSON；非 volatility_grid 返回 (nil,false)；
// 解析失败回退默认值。对应 C# VolatilityGridExecutionRuleParser.TryParse。
// Go encoding/json 字段匹配大小写不敏感，对齐 C# PropertyNameCaseInsensitive。
func TryParseVolatilityGridRule(executionRuleJSON string) (*VolatilityGridExecutionRule, bool) {
	if executionRuleJSON == "" || executionRuleJSON == "{}" {
		return nil, false
	}
	var probe struct {
		Type string `json:"type"`
	}
	if err := json.Unmarshal([]byte(executionRuleJSON), &probe); err != nil {
		return nil, false
	}
	if probe.Type != "volatility_grid" {
		return nil, false
	}

	var dto volatilityGridDTO
	if err := json.Unmarshal([]byte(executionRuleJSON), &dto); err != nil {
		d := DefaultVolatilityGridRule()
		return &d, true
	}

	rule := VolatilityGridExecutionRule{
		Type:                   "volatility_grid",
		EntryVolatilityPercent: posOr(dto.EntryVolatilityPercent, decimal.NewFromInt(1)),
		RebalancePercent:       posOr(dto.RebalancePercent, decimal.NewFromInt(1)),
		BasePositionSize:       posOr(dto.BasePositionSize, decimal.NewFromInt(100)),
		MaxPositionSize:        posOr(dto.MaxPositionSize, decimal.NewFromInt(500)),
		MaxPyramidingLevels:    intOr(dto.MaxPyramidingLevels, 5, dto.MaxPyramidingLevels >= 0),
		NoStopLoss:             dto.NoStopLoss,
		SlippageTolerance:      nonNegOr(dto.SlippageTolerance, decimal.NewFromFloat(0.0005)),
		MaxDailyLoss:           nonNegOr(dto.MaxDailyLoss, decimal.NewFromInt(200)),
	}
	return &rule, true
}

func posOr(v, def decimal.Decimal) decimal.Decimal {
	if v.IsPositive() {
		return v
	}
	return def
}

func nonNegOr(v, def decimal.Decimal) decimal.Decimal {
	if !v.IsNegative() {
		return v
	}
	return def
}

func intOr(v, def int, ok bool) int {
	if ok {
		return v
	}
	return def
}

// VolatilityGridAction 网格决策动作。
type VolatilityGridAction int

const (
	GridHold VolatilityGridAction = iota
	GridBuy
	GridSell
)

// VolatilityGridDecision 网格决策结果。
type VolatilityGridDecision struct {
	Action   VolatilityGridAction
	Quantity decimal.Decimal
	Reason   string
	NewLevel int
}

// VolatilityGridState 网格状态（均价/总量/加仓档位）。
type VolatilityGridState struct {
	AverageEntryPrice decimal.Decimal
	QuantityHeld      decimal.Decimal
	PyramidingLevel   int
}

// VolatilityGridExecutor 波动率网格核心算法。对应 C# VolatilityGridExecutor。
type VolatilityGridExecutor struct {
	rule VolatilityGridExecutionRule
}

// NewVolatilityGridExecutor 构造执行器。
func NewVolatilityGridExecutor(rule VolatilityGridExecutionRule) *VolatilityGridExecutor {
	return &VolatilityGridExecutor{rule: rule}
}

// Decide 给出加仓/减仓/持有决策。
func (e *VolatilityGridExecutor) Decide(state VolatilityGridState, currentPrice decimal.Decimal) VolatilityGridDecision {
	if !currentPrice.IsPositive() {
		return hold("非法价格")
	}
	if !state.QuantityHeld.IsPositive() {
		qty := e.rule.BasePositionSize.Div(currentPrice)
		return VolatilityGridDecision{GridBuy, qty, "首次入场 (level=1)", 1}
	}
	avg := state.AverageEntryPrice
	if !avg.IsPositive() {
		return hold("均价无效")
	}
	deviationPct := currentPrice.Sub(avg).Div(avg).Mul(decimal.NewFromInt(100))

	if deviationPct.LessThanOrEqual(e.rule.RebalancePercent.Neg()) {
		if state.PyramidingLevel >= e.rule.MaxPyramidingLevels {
			return hold(fmt.Sprintf("已达加仓上限 (%d)", e.rule.MaxPyramidingLevels))
		}
		projectedNotional := state.QuantityHeld.Mul(avg).Add(e.rule.BasePositionSize)
		if projectedNotional.GreaterThan(e.rule.MaxPositionSize) {
			return hold(fmt.Sprintf("加仓后名义价值 %s 超过上限 %s", projectedNotional.StringFixed(2), e.rule.MaxPositionSize.StringFixed(2)))
		}
		qty := e.rule.BasePositionSize.Div(currentPrice)
		return VolatilityGridDecision{GridBuy, qty, fmt.Sprintf("下跌 %s%% 触发加仓", deviationPct.StringFixed(2)), state.PyramidingLevel + 1}
	}

	if deviationPct.GreaterThanOrEqual(e.rule.RebalancePercent) {
		sellQty := decimal.Min(state.QuantityHeld, e.rule.BasePositionSize.Div(currentPrice))
		return VolatilityGridDecision{GridSell, sellQty, fmt.Sprintf("上涨 %s%% 触发减仓", deviationPct.StringFixed(2)), max(0, state.PyramidingLevel-1)}
	}

	return hold(fmt.Sprintf("偏离 %s%% 未达 %s%%", deviationPct.StringFixed(2), e.rule.RebalancePercent.String()))
}

func hold(reason string) VolatilityGridDecision {
	return VolatilityGridDecision{GridHold, decimal.Zero, reason, 0}
}
