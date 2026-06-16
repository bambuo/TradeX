# 修复预设策略 JSON 配置 Spec

## Why
4 个前端预设策略 JSON 文件（ma-crossover、macd-crossover、breakout-strategy、martingale）的节点配置与后端实际运行时逻辑不匹配，导致所有 Action 节点在运行时无法触发交易决策。

## What Changes
- 修改 4 个预设 JSON 的节点选择与参数，使其与后端实际逻辑一致
- `signal_action` 改为使用信号管线中的信号名（如 SMA_20、MACD_LINE 的穿越用 native gate 替代）
- `trend_action` 仅在空仓场景使用，退出用 `signal_action`
- 移除出场链中的阻断型 Risk 节点（`daily_loss_limit`、`max_drawdown`）

## Impact
- Affected specs: 无现有 spec
- Affected code: `frontend/src/assets/presets/*.json`

---

## ADDED Requirements

### Requirement: signal_action 使用信号管线原生信号
`signal_action` 运行时从 `ChainState.Signals` 读取信号值，而非 `ChainState.DerivedValues`。预设中的 `buySignal`/`sellSignal` **SHALL** 引用信号管线中已注册的信号名称。

`crossover_check` 节点的输出写入 `DerivedValues`，**不可**被 `signal_action` 读取。

#### Scenario: MA 金叉入场 — 用 signal_gate 替代 crossover_check 输出
- **GIVEN** 后端只有信号管线信号在 `state.Signals` 中
- **WHEN** 预设配置 `signal_action(buySignal:"SMA_20")` 且 `signal_gate` 串联检测 SMA_20 > SMA_50
- **THEN** `signal_action` 能读到 SMA_20 信号值并触发 BUY

### Requirement: trend_action 仅用于空仓开仓场景
`trend_action` 运行时仅处理 `!hasPosition`（空仓）分支，持仓时直接返回不产出任何决策。

预设中的 `trend_action` **SHALL NOT** 出现在 `position_gate(OPEN)` 链中。

#### Scenario: 马丁加仓链 — 用 signal_action 替代 trend_action
- **GIVEN** `trend_action` 在持仓时不产出任何决策
- **WHEN** 加仓链需要检测 DEVIATION_FROM_AVG <= -2 时加仓
- **THEN** 用 `signal_action(buySignal:"DEVIATION_FROM_AVG", direction:BELOW, threshold:-2)` 替代

### Requirement: 出场链中无阻断型 Risk 节点
`daily_loss_limit` 和 `max_drawdown` 在 Risk 阶段清空所有 Actions（含 SELL），在出场链中会阻断平仓。

预设的出场链 **SHALL NOT** 包含 `daily_loss_limit` 或 `max_drawdown`。

#### Scenario: MA 死叉出场 — 移除 daily_loss_limit
- **GIVEN** 出场链当前有 `daily_loss_limit(200)` 在 Risk 阶段
- **WHEN** 日亏损超过 200 USDT 且死叉信号触发
- **THEN** 移除 `daily_loss_limit`，确保 SELL 不被阻断

---

## MODIFIED Requirements
无。

## REMOVED Requirements
无。
