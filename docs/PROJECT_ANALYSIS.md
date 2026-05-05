# TradeX 项目分析

## 基本信息

| 项目 | 内容 |
|------|------|
| 系统名称 | TradeX |
| 定位 | 多交易所现货自动交易系统 |
| 后端 | ASP.NET Core 10 + C# 14 |
| 前端 (主) | Vue 3 + TypeScript + Vite 8 + Pinia + Arco Design |
| 前端 (并行) | Blazor Server + Fluent UI v5 |
| 部署 | Docker Compose 单容器 (+ IoTDB 独立容器) |
| 数据库 | SQLite (配置类数据) + IoTDB (时序 K 线数据) |

---

## 目录结构

```
TradeX/
├── AGENTS.md              # AI 编码规范（入口，指向 ROLE.md）
├── ROLE.md                # 多角色分工协作规范（核心）
├── docker-compose.yml     # tradex + iotdb 编排
├── Dockerfile             # 三阶段构建
├── backend/
│   ├── TradeX.slnx        # 解决方案文件（9 个项目）
│   ├── TradeX.Api/        # REST API 入口
│   │   ├── Controllers/   # 16 个控制器
│   │   ├── Hubs/          # TradingHub (SignalR)
│   │   ├── Middleware/    # 5 个中间件
│   │   └── Services/      # JWT、MFA、SignalR 事件总线
│   ├── TradeX.Blazor/     # Blazor Server UI
│   │   ├── Components/    # Razor 页面组件
│   │   └── Services/      # 页面服务层
│   ├── TradeX.Core/       # 领域模型（零依赖叶节点）
│   ├── TradeX.Exchange/   # 交易所客户端抽象与实现
│   ├── TradeX.Indicators/ # 技术指标封装
│   ├── TradeX.Trading/    # 策略引擎 + 风控 + 回测
│   ├── TradeX.Infrastructure/ # EF Core + Casbin + IoTDB
│   ├── TradeX.Notifications/  # 通知发送
│   └── TradeX.Tests/      # 测试项目
├── frontend/              # Vue 3 SPA
│   ├── src/api/           # 15 个 API 模块
│   ├── src/views/         # 15 个页面视图
│   ├── src/components/    # 公共组件
│   └── src/router/        # 路由 + 角色守卫
└── docs/
    ├── PRD.md             # 产品需求文档 v2.6
    ├── FSD.md             # 功能规格文档 v1.10
    ├── TAD.md             # 技术架构文档 v1.3
    └── TestCases.md       # 测试用例
```

---

## 后端项目依赖关系

```
TradeX.Api
  ├── TradeX.Trading
  │   ├── TradeX.Core (叶节点，零依赖)
  │   ├── TradeX.Exchange → TradeX.Core
  │   ├── TradeX.Indicators → TradeX.Core
  │   └── TradeX.Infrastructure → TradeX.Core
  ├── TradeX.Exchange → TradeX.Core
  ├── TradeX.Indicators → TradeX.Core
  ├── TradeX.Infrastructure → TradeX.Core
  └── TradeX.Notifications → TradeX.Core

TradeX.Blazor → TradeX.Api (代理)
TradeX.Tests (引用全部项目)
```

依赖方向：`Api → Trading → Exchange, Indicators, Infrastructure, Notifications`。Core 不依赖任何项目。

---

## 核心业务实体

| 实体 | 说明 |
|------|------|
| **User** | 用户账号，四级角色 (SuperAdmin/Admin/Operator/Viewer)，强制 MFA |
| **Trader** | 交易员，系统核心业务实体，绑定交易所和策略 |
| **Exchange** | 交易所配置，API Key AES-256 加密存储 |
| **Strategy** | 策略模板（条件树 + 执行规则），可复用 |
| **StrategyDeployment** | 策略部署（模板绑定到 Trader/Exchange/Symbol 作用域） |
| **Position** | 运行中持仓 |
| **Order** | 订单记录 |
| **BacktestTask** | 回测任务（异步执行） |
| **BacktestResult** | 回测绩效结果 |
| **Symbol** | 交易对（如 BTCUSDT） |
| **NotificationChannel** | 通知渠道 (Telegram/Discord/Email) |
| **AuditLog** | 审计日志 |
| **SystemConfig** | 系统配置键值对 |
| **RefreshToken** | 刷新令牌 |

---

## API 模块总览（16 个 Controller）

| Controller | 路由前缀 | 角色要求 |
|-----------|----------|---------|
| HealthController | `GET /health` | 公开 |
| SetupController | `/api/setup/*` | 公开（仅初始化） |
| AuthController | `/api/auth/*` | 部分公开 |
| UsersController | `/api/users/*` | Admin+ |
| TradersController | `/api/traders/*` | Operator+ |
| ExchangesController | `/api/exchanges/*` | Operator+ |
| GlobalStrategiesController | `/api/strategies/*` | Operator+ |
| TradersStrategiesController | `/api/traders/{tid}/strategies/*` | Operator+ |
| PositionsController | `/api/traders/{tid}/positions/*` | 全部认证用户 |
| OrdersController | `/api/traders/{tid}/orders/*` | 全部认证用户 |
| BacktestingController | `/api/traders/{tid}/strategies/{sid}/backtests/*` | Operator+ |
| DashboardController | `/api/dashboard/*` | 全部认证用户 |
| NotificationChannelsController | `/api/notifications/channels/*` | Admin+ |
| AuditLogsController | `/api/audit-logs` | Admin+ |
| SettingsController | `/api/settings` | Admin+ |
| SystemController | `/api/system/*` | Admin+ |

---

## 关键设计模式

| 模式 | 位置 | 说明 |
|------|------|------|
| Chain of Responsibility | `RiskHandlers.cs` | 7 节点风控管线（滑点→日亏损→回撤→连续亏损→熔断→冷却→持仓上限） |
| Factory | `ExchangeClientFactory.cs` | 按 ExchangeType 创建对应交易所客户端 |
| Strategy + DI | `IConditionEvaluator` | 可替换条件评估算法 |
| Repository | `Data/Repositories/` | 12 个 Repository 实现数据持久化抽象 |
| BackgroundService | `TradingEngine.cs` | 策略评估循环 15s/cycle |
| Decorator | `AuditProxy.cs` | Blazor 侧自动审计日志包装 |
| Options 模式 | 各 Settings 类 | 强类型配置绑定 |
| 资源感知调度 | `BacktestScheduler + ResourceMonitor` | 回测并发数动态调整（内存+CPU 水位） |

---

## 中间件管线

```
Request
  → ExceptionHandlingMiddleware
  → SetupGuardMiddleware（初始化守卫）
  → IpWhitelistMiddleware（IP 白名单）
  → JwtAuthMiddleware（JWT 验签）
  → CasbinAuthorizationMiddleware（RBAC 鉴权）
  → AuditLogMiddleware（审计日志）
    → Controller
```

---

## SignalR 事件合约

| 事件 | 方向 | 触发时机 |
|------|------|---------|
| `PositionUpdated` | Server→Client | 持仓更新/关闭 |
| `OrderPlaced` | Server→Client | 下单/成交/取消 |
| `StrategyStatusChanged` | Server→Client | 策略状态机转换 |
| `RiskAlert` | Server→Client | 风控链拦截 |
| `DashboardSummary` | Server→Client | 定时推送 (15s) |
| `ExchangeConnectionChanged` | Server→Client | WS 断线/重连 |

---

## 策略状态机

```
Draft → Backtesting → Passed → Active → Disabled
                        ↑         ↓         ↓
                        └─────────┴── Draft ←┘
```

约束：
- 仅 `Passed` 可切换为 `Active`
- 同一 Trader 在同一 Exchange 上对同一 Symbol 仅允许 1 个 Active 策略
- 首次回测通过后从 Draft→Passed；后续回测不改变状态
- Volatility Grid 遵循同一状态机，增加运行时约束（金字塔加仓上限、波幅触发等）

---

## 策略部署作用域（三层级）

| 作用域 | 优先级 | 配置条件 |
|--------|--------|---------|
| Symbol | 高 | TraderId + ExchangeId + SymbolIds |
| Exchange | 中 | TraderId + ExchangeId |
| Trader | 低 | TraderId |

---

## 风控架构（四层叠加）

1. **系统级** — Kill Switch、总敞口上限、日亏损上限、最大回撤、看门狗
2. **交易员级** — 敞口上限、日亏损上限、活跃策略数上限、最大回撤
3. **交易所级** — 可交易余额上限、API 健康度、单日交易量上限、币种集中度
4. **币种级** — 单币种持仓上限、日交易次数上限

任一层级触发即停止对应范围内的交易，各层级独立恢复。

---

## 回测引擎

- 逐 K 线回放，共享实盘条件树评估逻辑
- 异步执行（`BacktestTaskQueue` + `BacktestWorker` 后台 Worker）
- 资源感知调度（`ResourceMonitor` 每 5s 采样内存+CPU，动态限制并发数）
- SSE 实时推送分析数据（运行中走 Channel，已完成走 DB + 客户端定时器）
- 产出指标：总收益率、年化收益率、最大回撤、胜率、夏普比率、盈亏比
- K 线分析数据中包含逐根 K 线的指标值，支持结果回放

---

## ADR 索引（11 项）

| # | 决策 | 状态 |
|---|------|------|
| 001 | SQLite 为主存储 | 已采纳 |
| 002 | IoTDB 为时序数据库 | 已采纳 |
| 003 | Casbin.NET 为鉴权引擎 | 已采纳 |
| 004 | Chain of Responsibility 为风控模式 | 已采纳 |
| 005 | Trading Engine 为单实例 BackgroundService | 已采纳 |
| 006 | JKorf 系列为交易所 SDK | 已采纳 |
| 007 | Skender.Stock.Indicators 为指标库 | 已采纳 |
| 008 | AES-256-GCM 为凭证加密算法 | 已采纳 |
| 009 | JSON 字段统一 camelCase | 已采纳 |
| 010 | SSE 为回测 K 线实时推送协议 | 已采纳 |
| 011 | 回测并发调度采用资源感知调度 | 已采纳 |

---

## 已支持交易所

| 交易所 | SDK | WS 行情 | 交易 |
|--------|-----|---------|------|
| Binance | Binance.Net | ✅ | ✅ |
| OKX | JK.OKX.Net | ✅ | ✅ |
| Gate.io | GateIo.Net | ✅ | ✅ |
| Bybit | Bybit.Net | ✅ | ✅ |
| HTX | JKorf.HTX.Net | ✅ | ✅ |

---

## 技术指标（首批 8 个）

RSI、MACD、SMA、EMA、Bollinger Bands、Volume SMA、OBV、KDJ

封装于 `TradeX.Indicators`，基于 Skender.Stock.Indicators 库。

---

## 通知渠道

| 渠道 | 协议 | 配置项 |
|------|------|--------|
| Telegram | Bot API | BotToken, ChatId |
| Discord | Webhook | WebhookUrl |
| Email | SMTP | Host, Port, Username, Password |

---

## 数据流

```
交易所 WebSocket
  → 内存缓存 (ConcurrentDictionary)
    → 指标计算 (Skender.Stock.Indicators)
      → 策略评估 (条件树)
        → 风控检查 (责任链 + 四层叠加)
          → 下单执行 (IExchangeClient)
            → 写入 Order/Position 数据库
              → SignalR 推送前端
              → 通知渠道推送
```

---

## 部署架构

```
Docker Compose:
  tradex (ASP.NET Core :80)
    ├── /api/*       → REST Controllers
    ├── /hubs/trading → SignalR Hub
    ├── /health      → Health Check
    ├── /_blazor/*   → Blazor Server SignalR
    └── /*           → Blazor 页面路由 / SPA 静态文件

  iotdb (Apache IoTDB 1.3.3 :6667/:8181)
```

持久卷：`tradex-data`（SQLite）、`iotdb-data`、`iotdb-wal`

---

## 代码量估算

| 模块 | 文件数 | 说明 |
|------|--------|------|
| TradeX.Core | ~46 | Models(17) + Enums(9) + Interfaces(19) + Attributes(1) |
| TradeX.Api | ~27 | Controllers(16) + Middleware(5) + Services(3) + Hubs(1) + Settings(1) + Program |
| TradeX.Blazor | ~14 | Services(12) + Program |
| TradeX.Exchange | ~8 | 5 客户端 + Factory + RateLimiter + DI |
| TradeX.Indicators | ~4 | Service + Interface + DI |
| TradeX.Trading | ~27 | Engine + Risk + Backtest + Reconciler + Rules |
| TradeX.Infrastructure | ~40 | DbContext + Repos(12) + Migrations(16) + Casbin + IoTDB + Encryption |
| TradeX.Notifications | ~8 | Service + 3 发送器 + DI + Settings |
| TradeX.Tests | ~16 | 测试文件 |
| frontend (Vue) | ~44 | 15 views + 15 api + 8 components + router + stores + layouts |
| **合计** | **~234** | |

---

## 团队协作规范摘要

项目通过 `AGENTS.md` + `ROLE.md` 定义了 6 角色（PM、Architect、Backend、Frontend、QA、DevOps）的多角色协作流程：

- **PM 启动** → 任务拆解 → 匹配执行链路
- **角色激活** → 每次切换输出激活声明（职责/边界/禁止事项）
- **闭环自检** → 5 维度检查（全部 ✅ 方可提交）
- **角色交接** → 格式化的交付物清单 + 级联上下文摘要
- **违规熔断** → 三级违规处理机制
- **全生命周期** → 9 种任务状态，从待拆解到已关闭
- **上下文预算控制** → 级联摘要 ≤200 tokens/角色

---

## 启动方式

```bash
# Docker 部署
JWT_SECRET=your-secret-key docker compose up --build

# 本地开发 - 后端
cd backend && dotnet run --project TradeX.Api

# 本地开发 - 前端 (Vue SPA)
cd frontend && npm run dev

# 运行测试
cd backend && dotnet test
```
