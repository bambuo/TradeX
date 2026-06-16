# 修复规则链引擎审计 Bug Spec

## Why
对 45 个 `IRuleNode` + `ChainEngine` + `ChainCoordinator` 的完整代码审计发现 10 个严重 Bug，涉及信号读取路径错误、有状态节点不回写、做空方向错误等，导致多个核心节点运行时行为与预期不符。

## What Changes
- **BREAKING** `regime_gate`: `AllowedRegimes` 枚举值变更为数字字符串 `["0".."4"]` 以匹配信号实际输出
- 修复 2 个有状态节点不写回 StateStore: `dca_action` (`lastAt`), `martingale_action` (`step`)
- 修复做空方向: `trailing_stop_action`, `emergency_exit`
- 修复 `grid_size`/`grid_action` 不匹配、`grid_action` 零数量 Action
- 修复 `cost_anchored_rebalance` 锚点不回写
- 修复 `cooldown` 冷却期阻止 SELL
- 修复 `signal_gate`/`position_gate` 未知参数静默放行

## Impact
- Affected specs: 无
- Affected code: `backend/TradeX.Trading/Rules/Nodes/*.cs`
