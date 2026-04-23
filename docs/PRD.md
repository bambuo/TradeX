# BSC Smart Money Trading Bot PRD

## 1. 文档信息
- 文档版本: v1.0
- 文档状态: Draft
- 更新时间: 2026-04-23
- 适用项目: `BscSmartMoneyBot`

## 2. 背景与问题
当前团队需要一个可持续运行、可配置、可观测的自动交易机器人，用于基于链上聪明钱信号执行策略交易。  
核心痛点：
1. 信号识别、过滤与下单流程分散，缺少统一闭环。
2. 风控与退出策略缺乏标准化（止损、止盈、移动止损、分批止盈）。
3. 状态管理和恢复机制不统一，重启后连续性差。
4. 发布与运行方式不一致，环境依赖高。

## 3. 产品目标
1. 建立“信号获取 -> 风控过滤 -> 下单执行 -> 持仓管理 -> 状态持久化”的自动化闭环。
2. 支持实盘与 `dry-run` 双模式，保证可安全演练。
3. 在配置可控前提下实现可扩展策略框架（仓位、滑点、退出策略）。
4. 通过解决方案化工程结构降低维护成本。

## 4. 范围定义
### 4.1 本期范围（In Scope）
1. 聪明钱信号轮询与新信号识别。
2. 基础过滤（市值、流动性、卖出比例、聪明钱包数）。
3. 安全扫描与风险等级拦截。
4. 买入、卖出、分批卖出执行。
5. 止损、止盈、分批止盈、动态移动止损。
6. 状态持久化、备份与恢复。
7. 配置中心（`appsettings*.json` + CLI 覆盖）。
8. 单文件发布与本地可运行测试工程。

### 4.2 非本期范围（Out of Scope）
1. 可视化 Web 控制台。
2. 多策略并行编排与回测系统。
3. 多账户/多钱包资产分账管理。
4. 交易所/链路多供应商路由。

## 5. 目标用户与使用场景
### 5.1 用户角色
1. 交易策略操作者：配置参数并启动机器人。
2. 运维/开发人员：排查日志、维护状态文件、发布版本。

### 5.2 核心场景
1. 日常自动运行：按轮询周期持续发现并处理新信号。
2. 安全演练：通过 `--dry-run` 观察行为不落链。
3. 异常恢复：重启后读取状态继续运行。

## 6. 功能需求（FR）
### FR-01 信号采集
1. 系统必须按配置链路采集聪明钱信号。
2. 系统必须识别“新信号”并去重，避免重复入场。

### FR-02 信号过滤
1. 系统必须支持市值、流动性、卖出比例、聪明钱包数阈值过滤。
2. 过滤结果必须可记录日志。

### FR-03 安全扫描
1. 系统必须执行 token 安全扫描。
2. 对 `RiskLevelBuyBlock` 的风险等级必须阻断买入。
3. 对 `RiskLevelBuyPause` 的风险等级必须暂停并记录日志。

### FR-04 买入执行
1. 系统必须在满足冷却期、最大持仓、余额条件后执行买入。
2. 系统必须支持策略化推荐买入金额。
3. 系统必须支持智能滑点计算。

### FR-05 卖出执行
1. 系统必须支持全量卖出和分批卖出。
2. 系统必须记录交易后盈亏统计并更新状态。

### FR-06 退出策略
1. 系统必须支持止损触发卖出。
2. 系统必须支持止盈触发卖出。
3. 系统必须支持分批止盈策略。
4. 系统必须支持动态移动止损。

### FR-07 状态管理
1. 系统必须持久化以下关键状态：已见信号、当前持仓、统计指标。
2. 系统必须支持状态备份。
3. 状态损坏时系统必须降级为安全默认状态（新状态）。

### FR-08 运行模式
1. 系统必须支持 `DryRun=true/false`。
2. 命令行参数必须可以覆盖配置文件关键字段。

## 7. 非功能需求（NFR）
1. 技术栈：`.NET 10` + `C# preview`（C#14）。
2. 稳定性：公共边界方法异常可记录并返回可控结果；内部关键链路异常可上抛。
3. 可维护性：主/测工程独立，解决方案入口统一为 `BscSmartMoneyBot.slnx`。
4. 可观测性：日志必须使用结构化日志模板。
5. 可测试性：测试工程可通过 `dotnet test BscSmartMoneyBot.slnx` 执行。

## 8. 业务流程
1. 启动服务并加载配置与状态。
2. 周期轮询：
   - 更新持仓价格
   - 拉取新信号
   - 基础过滤
   - 安全扫描
   - 执行买入
   - 管理持仓（止盈止损）
   - 计算下轮轮询间隔
3. 停止时保存最终状态。

## 9. 配置与关键参数
1. 信号参数：`MinMarketCap`、`MinLiquidity`、`MaxSoldRatio`、`MinSmartMoneyWallets`
2. 交易参数：`MaxOpenPositions`、`CooldownMinutes`、`MinPositionSizeUSD`、`MaxPositionSizeUSD`
3. 风控参数：`StopLossPercent`、`TakeProfitPercent`、`TrailingStopPercent`
4. 滑点参数：`MinSlippagePercent`、`MaxSlippagePercent`
5. 安全参数：`RiskLevelBuyBlock`、`RiskLevelBuyPause`

## 10. 验收标准（Acceptance Criteria）
1. 运行 `dotnet build BscSmartMoneyBot.slnx` 无错误。
2. 运行 `dotnet test BscSmartMoneyBot.slnx` 全通过。
3. `dry-run` 模式下可完整跑通信号到交易流程，不执行真实链上交易。
4. 触发止损/止盈/分批止盈/动态移动止损时，行为与配置一致且有日志。
5. 重启后可读取历史状态继续执行。

## 11. 风险与约束
1. 外部命令/供应商可用性波动（`onchainos`）。
2. 链上价格与交易结果可能存在延迟与滑点偏差。
3. 参数配置不当会放大风险（如仓位过大、阈值过宽）。

## 12. 里程碑建议
1. M1（已完成）：核心闭环与工程结构稳定化。
2. M2：补齐异常路径与边界条件测试覆盖。
3. M3：增强运行监控与操作手册（告警、回滚、恢复演练）。

## 13. 当前版本实现映射（代码）
1. 启动与DI：`BscSmartMoneyBot/Program.cs`、`BscSmartMoneyBot/Configuration/HostBuilderExtensions.cs`
2. 业务编排：`BscSmartMoneyBot/HostedServices/TradingBotHostedService.cs`
3. 信号处理：`BscSmartMoneyBot/Services/Implementations/Trading/SignalMonitor.cs`
4. 执行与风控：`BscSmartMoneyBot/Services/Implementations/Trading/TradeExecutor.cs`、`PositionManager.cs`
5. 策略：`BscSmartMoneyBot/Services/Implementations/Trading/Strategies/*`
6. 状态：`BscSmartMoneyBot/Services/Implementations/Persistence/StateManager.cs`
7. 测试：`BscSmartMoneyBot.Tests/Services/*`
