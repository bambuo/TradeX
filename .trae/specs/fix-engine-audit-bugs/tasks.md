# Tasks

- [x] Task 1: 修复 `regime_gate` — 数字→枚举名映射
  - 文件: GateNodes.cs
  - `((int)regimeSig.Value).ToString()` → 映射数组转 RANGING/TRENDING/...
  - Enum 保持 `["RANGING","TRENDING","HIGH_VOL","CRASH","LOW_VOL"]`

- [x] Task 2: 修复有状态节点不写回 — `dca_action` + `martingale_action`
  - 文件: ActionNodes.cs
  - dca_action: `await store.WriteStateAsync` 写入 lastAt
  - martingale_action: step++ + `await store.WriteStateAsync`

- [x] Task 3: 修复做空方向 — `trailing_stop_action` + `emergency_exit`
  - 文件: ActionNodes.cs, OverrideNodes.cs
  - trailing_stop_action: `isLong ? "SELL" : "BUY"`
  - emergency_exit: `isLong ? "SELL_ALL" : "BUY"`

- [x] Task 4: 修复 `grid_action` — 空 SizeDecisions 时不产出 Action
  - 文件: ActionNodes.cs
  - 添加 `if (quantity > decimal.Zero)` 守卫

- [x] Task 5: 修复 `cost_anchored_rebalance` — 首次写入 anchoredCost
  - 文件: CostAnchorNode.cs
  - EntryPrice 初始化后 `await store.WriteStateAsync`

- [x] Task 6: 修复 `cooldown` — 冷却期不阻止 SELL
  - 文件: RiskNodes.cs
  - `RemoveAll` 仅匹配 `"BUY"`

- [x] Task 7: 修复 `signal_gate` / `position_gate` — 未知参数 fail-closed
  - 文件: GateNodes.cs
  - signal_gate: 未知 Op `_ => false`
  - position_gate: 未知 Require `_ => true` (blocked)

- [x] Task 8: 构建验证
  - `dotnet build TradeX.Trading` + `dotnet build TradeX.Api` 通过
