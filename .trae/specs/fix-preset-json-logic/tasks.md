# Tasks

- [x] Task 1: 修复双均线趋势策略 (ma-crossover.json)
  - 入场链：RSI < 65 gate + signal_action(buySignal:"RSI", BELOW, 45)，使用管线原生信号
  - 出场链：signal_action(sellSignal:"RSI", ABOVE, 75)，无阻断型 Risk

- [x] Task 2: 修复 MACD 趋势策略 (macd-crossover.json)
  - 入场链：MACD_LINE > 0 gate + signal_action(buySignal:"MACD_LINE", ABOVE, 0)，使用管线原生信号
  - 出场链：signal_action(sellSignal:"RSI", ABOVE, 75)，无阻断型 Risk

- [x] Task 3: 修复波动率突破 (breakout-strategy.json)
  - 入场链：RANGE_PCT >= 1.5 gate + RSI > 40 gate + signal_action(buySignal:"RANGE_PCT", ABOVE, 1.5)
  - 入场链保留 max_drawdown、daily_loss_limit、consecutive_loss_stop
  - 出场链：signal_action(sellSignal:"RSI", ABOVE, 80)，无阻断型 Risk

- [x] Task 4: 修复马丁格尔 (martingale.json)
  - 首仓链：signal_action(buySignal:"RSI", BELOW, 40) + max_drawdown(5%)
  - 加仓链：signal_action(buySignal:"DEVIATION_FROM_AVG", BELOW, -2) + max_pyramiding(4)
  - 出场链：signal_action(sellSignal:"DEVIATION_FROM_AVG", ABOVE, 3)，无阻断型 Risk

- [x] Task 5: 构建验证
  - `npx vite build` 通过

# Task Dependencies
- Task 5 depends on Task 1,2,3,4
- Task 1-4 相互独立，可并行
