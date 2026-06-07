package trading

import "github.com/shopspring/decimal"

// RiskSettings 移植自 C# RiskSettings。默认值与 C# 完全一致。
type RiskSettings struct {
	// 风控阈值。
	MaxDailyLoss         decimal.Decimal
	MaxDrawdownPercent   decimal.Decimal
	MaxConsecutiveLosses int
	MaxOpenPositions     int
	SlippageTolerance    decimal.Decimal
	MaxSlippageAmount    decimal.Decimal
	MaxSlippagePercent   decimal.Decimal
	CircuitBreakerActive bool
	CooldownSeconds      int
	MaxOrderNotional     decimal.Decimal

	// 策略评估并行度（每 trader 顺序、多 trader 并行）。
	StrategyEvaluationParallelism int

	// OrderReconciler 巡检周期（秒）。
	OrderReconcileIntervalSeconds int
	// Pending 订单"陈旧"阈值（分钟）。
	StalePendingMinutes int

	// 持仓级对账。
	PositionReconcileEnabled         bool
	PositionReconcileIntervalSeconds int
	PositionDriftTolerancePercent    decimal.Decimal
	PositionDriftMinAbsolute         decimal.Decimal
	PositionDriftReportSurplus       bool

	// 计价资产清单，用于从交易对切出 base 资产（按长度降序匹配后缀）。
	QuoteAssets []string
}

// DefaultRiskSettings 返回与 C# RiskSettings 默认值一致的设置。
func DefaultRiskSettings() RiskSettings {
	return RiskSettings{
		MaxDailyLoss:                     decimal.NewFromInt(1000),
		MaxDrawdownPercent:               decimal.NewFromInt(20),
		MaxConsecutiveLosses:             3,
		MaxOpenPositions:                 10,
		SlippageTolerance:                decimal.NewFromFloat(0.001),
		MaxSlippageAmount:                decimal.NewFromInt(10),
		MaxSlippagePercent:               decimal.NewFromFloat(1.0),
		CircuitBreakerActive:             false,
		CooldownSeconds:                  300,
		MaxOrderNotional:                 decimal.Zero,
		StrategyEvaluationParallelism:    4,
		OrderReconcileIntervalSeconds:    60,
		StalePendingMinutes:              5,
		PositionReconcileEnabled:         true,
		PositionReconcileIntervalSeconds: 300,
		PositionDriftTolerancePercent:    decimal.NewFromFloat(1.0),
		PositionDriftMinAbsolute:         decimal.Zero,
		PositionDriftReportSurplus:       false,
		QuoteAssets:                      []string{"USDT", "USDC", "FDUSD", "TUSD", "BUSD", "DAI", "BTC", "ETH", "BNB", "EUR", "TRY"},
	}
}
