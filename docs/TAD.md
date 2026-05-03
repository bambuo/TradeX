# TradeX — TAD (Technical Architecture Document)

## 1. 文档信息

| 项目 | 内容 |
|---|---|
| 文档版本 | v1.3 |
| 文档状态 | Draft |
| 基于 PRD | `docs/PRD.md` v2.6 |
| 基于 FSD | `docs/FSD.md` v1.10 |
| 更新时间 | 2026-04-27 |
| 适用阶段 | 立项评审 / M1-M7 研发 |

---

## 2. 架构原则与约束

### 2.1 架构原则

| # | 原则 | 说明 |
|---|------|------|
| P1 | **防御第一** | 所有外部输入视为不可信；资金操作必须过风控链；交易所 API 调用必须过限流器 |
| P2 | **离线韧性** | 每个交易所 WebSocket 断线、IoTDB 不可用、交易所 API 5xx，均不可导致系统崩溃；应有降级路径 |
| P3 | **可观测性内建** | 结构化日志、审计日志、Health 端点、风控事件，不是后加的功能 |
| P4 | **状态显式化** | 策略状态机、订单状态机、持仓状态机全部用枚举 + 状态转换表驱动，禁止 if/else 隐式状态 |
| P5 | **依赖单向** | `Api → Trading → Exchange / Indicators / Infrastructure / Notifications`，Core 是叶节点，零依赖 |
| P6 | **配置即代码** | 所有可调参数（风控阈值、限流参数、K 线预热天数）均为强类型 Options 绑定，禁止 hardcode |
| P7 | **数据一致性兜底** | 订单 Reconciliation 是启动时必执行流程；本地状态以交易所为准，但交易所不可用时以本地为准 |

### 2.2 外部约束

| 约束 | 来源 |
|------|------|
| 目标框架 `net10.0` + `LangVersion=14.0` | AGENTS.md |
| 前端 Blazor Server + Fluent UI `Microsoft.FluentUI.AspNetCore.Components` v5.0 | AGENTS.md |
| SQLite 为主存储，IoTDB 为时序存储 | PRD FR-13 |
| Docker Compose 单机部署，无 K8s | PRD FR-16 |
| 仅支持现货交易，不涉及合约/杠杆 | FSD §25 待确认 #10 |
| 交易所 SDK 统一使用 JKorf 系列 | FSD v1.3 |

---

## 3. C4 模型

### 3.1 Level 1 — Context (系统上下文)

```mermaid
flowchart TB
  classDef person fill:#08427B,color:#fff,stroke:#073B6E,stroke-width:2px,rx:10px,ry:10px
  classDef system fill:#1168BD,color:#fff,stroke:#0D5BA3,stroke-width:2px
  classDef ext fill:#686868,color:#fff,stroke:#555,stroke-width:2px,stroke-dasharray:5 5

  ADMIN(["Admin / Operator<br/>后台管理人员，配置策略、监控交易"]):::person
  VIEWER(["Viewer<br/>只读用户，查看仪表盘与报表"]):::person

  TRADEX[TradeX<br/>多交易所现货自动交易系统]:::system

  BINANCE[Binance<br/>CEX REST + WebSocket]:::ext
  OKX[OKX<br/>CEX REST + WebSocket]:::ext
  GATE[Gate.io<br/>CEX REST + WebSocket]:::ext
  BYBIT[Bybit<br/>CEX REST + WebSocket]:::ext
  HTX[HTX<br/>CEX REST + WebSocket]:::ext

  TG[Telegram Bot API<br/>通知渠道]:::ext
  DC[Discord Webhook<br/>通知渠道]:::ext
  EM[SMTP Server<br/>通知渠道]:::ext

  ADMIN -->|"配置策略、管理交易、查看报表 (HTTPS/SignalR)"| TRADEX
  VIEWER -->|"查看仪表盘 (HTTPS/SignalR)"| TRADEX
  TRADEX -->|"行情订阅 + 交易执行 (WSS/REST)"| BINANCE
  TRADEX -->|"行情订阅 + 交易执行 (WSS/REST)"| OKX
  TRADEX -->|"行情订阅 + 交易执行 (WSS/REST)"| GATE
  TRADEX -->|"行情订阅 + 交易执行 (WSS/REST)"| BYBIT
  TRADEX -->|"行情订阅 + 交易执行 (WSS/REST)"| HTX
  TRADEX -->|"交易/风控告警推送 (HTTPS)"| TG
  TRADEX -->|"交易/风控告警推送 (HTTPS)"| DC
  TRADEX -->|"交易/风控告警推送 (SMTP)"| EM
```

### 3.2 Level 2 — Container (容器视图)

```mermaid
flowchart TB
  classDef person fill:#08427B,color:#fff,stroke:#073B6E,stroke-width:2px,rx:10px,ry:10px
  classDef container fill:#438DD5,color:#fff,stroke:#3377BB,stroke-width:2px
  classDef db fill:#F2F2F2,color:#333,stroke:#999,stroke-width:2px
  classDef ext fill:#686868,color:#fff,stroke:#555,stroke-width:2px,stroke-dasharray:5 5

  ADMIN(["用户<br/>浏览器"]):::person

  SQLITE[("SQLite<br/>EF Core")]:::db
  TRADEX[ASP.NET Core 10<br/>REST + SPA + SignalR + BackgroundService]:::container
  IOTDB[("IoTDB<br/>时序数据库")]:::db

  EXCHANGES[交易所集群<br/>Binance / OKX / Gate / Bybit / HTX]:::ext
  NOTIF[通知渠道<br/>Telegram / Discord / Email]:::ext

  ADMIN -->|"HTTP :80"| TRADEX
  TRADEX -->|"EF Core (ADO.NET)"| SQLITE
  TRADEX -->|"IoTDB REST (Thrift)"| IOTDB
  TRADEX -->|"行情 WSS + 交易 REST"| EXCHANGES
  TRADEX -->|"告警推送 (HTTPS/SMTP)"| NOTIF
```

#### 3.2.1 容器职责矩阵

| 容器 | 技术 | 实例数 | 状态持久化 | 扩缩容 |
|------|------|--------|-----------|--------|
| Backend (API+SPA) | ASP.NET Core 10 | 1 | 有状态（内存 K 线缓存） | 仅垂直（单实例 Trading Engine） |
| SQLite | Microsoft.Data.Sqlite | 1 | 文件持久卷 | N/A（单实例写） |
| IoTDB | apache/iotdb:1.3.3 | 1 | 文件持久卷 | 仅垂直（单实例） |

> **关键约束**：Trading Engine 作为 `BackgroundService` 维护策略评估循环和内存 K 线缓存，当前架构仅支持单后端实例。多实例部署需引入分布式缓存（Redis）和分布式锁，属于后期可演进方向。

#### 3.2.2 前端架构设计

| 方面 | 方案 | 说明 |
|------|------|------|
| 框架 | Blazor Server (InteractiveServer) | .NET 10 Interactive Server 渲染模式 |
| 组件库 | `Microsoft.FluentUI.AspNetCore.Components` v5.0 | Fluent Design System，Office/Microsoft 365 风格 |
| 路由 | `@page` 指令 + `FluentNav` | 声明式路由 + `AuthorizeView` 角色守卫 |
| 状态管理 | Blazor DI Singleton 服务 + `FluentDataGrid` | 按业务域拆分服务 |
| HTTP 客户端 | `HttpClient` + `IHttpClientFactory` | 内置 `Authorization` 消息处理器自动附加 JWT |
| 实时通信 | `HubConnection` + `@implements IAsyncDisposable` | 强类型 C# 订阅模式 |
| 图表 | ECharts + `ECharts` Blazor 封装 / 自绘组件 | K 线图、回测图表 |

**服务注册（Program.cs）**：

```csharp
builder.Services.AddFluentUIComponents();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// JWT 自动注入 HttpClient
builder.Services.AddScoped(sp =>
{
    var client = new HttpClient { BaseAddress = new Uri("/") };
    return client;
});

// SignalR HubConnection 工厂
builder.Services.AddSingleton<TradingHubClient>();
```

**页面路由与角色守卫**：

```razor
@* 页面定义示例 *@
@page "/exchanges"
@attribute [Authorize(Roles = "SuperAdmin,Admin,Operator")]

@* 角色守卫包装 *@
<AuthorizeView Roles="SuperAdmin,Admin">
    <Authorized>
        <FluentButton OnClick="@ShowAdminPanel">管理</FluentButton>
    </Authorized>
    <NotAuthorized>
        <FluentBadge Appearance="BadgeAppearance.Accent">仅管理员</FluentBadge>
    </NotAuthorized>
</AuthorizeView>
```

**Blazor 服务设计**：

```mermaid
flowchart LR
  subgraph SERVICES["Blazor 服务层 (DI Singleton/Scoped)"]
    AUTH[AuthService<br/>登录/MFA/Token 管理]
    TRADER[TraderService<br/>交易员列表/切换]
    EXCH[ExchangeService<br/>交易所配置/连接状态]
    STRAT[StrategyService<br/>策略 CRUD/状态管理]
    POS[PositionService<br/>持仓/实时更新]
    ORDER[OrderService<br/>订单历史/手动下单]
    DASH[DashboardService<br/>仪表盘汇总数据]
    NOTIF[NotificationService<br/>通知渠道配置]
    BACKTEST[BacktestService<br/>回测任务/结果]
    AUDIT[AuditLogService<br/>审计日志查询]
  end

  AUTH --> TRADER
  TRADER --> EXCH
  TRADER --> STRAT
  STRAT --> POS
  STRAT --> ORDER
  STRAT --> BACKTEST
  POS --> DASH
  ORDER --> DASH
```

**SignalR 集成模式**：

| Blazor 服务/组件 | 订阅事件 | 数据流向 |
|-----------------|---------|---------|
| `PositionService` | `PositionUpdated` | Hub → Service → Blazor 组件 `@onsomeone` 响应式更新 |
| `OrderService` | `OrderPlaced` | Hub → Service → 通知 |
| `StrategyService` | `StrategyStatusChanged` | Hub → Service → 状态刷新 |
| `DashboardService` | `DashboardSummary` | Hub → Service → KPI 卡片 |
| `ExchangeService` | `ExchangeConnectionChanged` | Hub → Service → 状态图标 |

**SignalR HubConnection 生命周期管理**：

```csharp
// TradingHubClient.cs — Singleton 服务
public sealed class TradingHubClient : IAsyncDisposable
{
    private HubConnection? _hubConnection;

    public async Task StartAsync(string accessToken)
    {
        _hubConnection = new HubConnectionBuilder()
            .WithUrl("/hubs/trading", options =>
                options.AccessTokenProvider = () => Task.FromResult(accessToken))
            .WithAutomaticReconnect()
            .Build();

        // 注册事件处理
        _hubConnection.On<object>("PositionUpdated", data => OnPositionUpdated?.Invoke(data));
        _hubConnection.On<object>("DashboardSummary", data => OnDashboardSummary?.Invoke(data));

        await _hubConnection.StartAsync();
    }

    public event Action<object>? OnPositionUpdated;
    public event Action<object>? OnDashboardSummary;
}
```

**前端页面文件结构**：

```
TradeX.Api/
└── Components/
    ├── Layout/
    │   ├── AppLayout.razor              # FluentLayout 整体布局
    │   └── MainNav.razor               # FluentNav 侧边导航
    ├── Pages/
    │   ├── SetupWizard.razor            # 初始化向导
    │   ├── Login.razor                  # 登录
    │   ├── MfaSetup.razor              # MFA 绑定
    │   ├── Dashboard.razor              # 仪表盘
    │   ├── Traders/
    │   │   ├── TraderList.razor        # 交易员列表
    │   │   └── TraderDetail.razor      # 交易员详情
    │   ├── Exchanges/
    │   │   ├── ExchangeList.razor      # 交易所列表
    │   │   └── ExchangeDetail.razor    # 交易所详情
    │   ├── Strategies/
    │   │   ├── StrategyList.razor      # 策略模板列表
    │   │   └── StrategyEditor.razor    # 策略编辑器（含条件树）
    │   ├── Positions.razor             # 持仓面板
    │   ├── Orders.razor                # 订单历史
    │   ├── Backtests/
    │   │   ├── BacktestList.razor      # 回测列表
    │   │   └── BacktestResult.razor    # 回测结果 + K 线回放
    │   ├── Notifications.razor         # 通知配置
    │   ├── AuditLogs.razor             # 审计日志
    │   ├── Users.razor                 # 用户管理
    │   └── Settings.razor              # 系统设置
    └── Shared/
        ├── ConditionTreeEditor.razor    # 条件树编辑器组件
        ├── ExecutionRuleEditor.razor    # 执行规则表单
        ├── ExchangeTypeSelect.razor     # 交易所类型下拉选择
        ├── ErrorBoundary.razor          # 错误边界
        └── BacktestCharts/
            ├── BacktestKlineChart.razor # K 线图表
            └── BacktestChart.razor     # 绩效图表
```

**条件编辑器架构**（Blazor 最复杂组件）：

```mermaid
flowchart TB
  subgraph Editor["策略条件编辑器"]
    PALLET[条件拖拽面板<br/>FluentSelect 指标选择 + FluentNumberField 值输入]
    CANVAS[条件树画布<br/>FluentTreeView / 递归嵌套]
    PREVIEW[条件预览<br/>JSON 实时显示]
    VALID[验证器<br/>树深度检查 + 类型检查]
    EXPORT[序列化器<br/>生成 EntryConditionJson]
  end

  PALLET --> CANVAS
  CANVAS --> VALID
  VALID --> EXPORT
  CANVAS --> PREVIEW
```

---

### 3.3 Level 3 — Component (组件视图)

#### 3.3.1 TradeX.Api 组件

```mermaid
flowchart TB
  classDef container fill:#438DD5,color:#fff,stroke:#3377BB,stroke-width:2px
  classDef component fill:#fff,color:#333,stroke:#438DD5,stroke-width:1px
  classDef bg fill:none,stroke:#438DD5,stroke-width:2px,stroke-dasharray:5 5

  subgraph ASP_NET["ASP.NET Core 10 Host"]
    CONTROLLERS[Controllers<br/>REST API]:::component
    HUBS[TradingHub<br/>SignalR Hub]:::component
    MIDDLEWARE[Middleware Pipeline]:::component

    MW1[SetupGuardMiddleware<br/>初始化守卫]:::component
    MW2[JwtAuthMiddleware<br/>JWT 验签]:::component
    MW3[CasbinAuthorizationMiddleware<br/>RBAC 鉴权]:::component
    MW4[AuditLogMiddleware<br/>审计日志]:::component

    ENGINE[TradingEngine<br/>BackgroundService<br/>策略评估循环 15s/cycle]:::component
  end

  CONTROLLERS -->|推送状态变更| HUBS
  CONTROLLERS -->|经过中间件管线| MIDDLEWARE

  MIDDLEWARE --> MW1
  MIDDLEWARE --> MW2
  MIDDLEWARE --> MW3
  MIDDLEWARE --> MW4

  ENGINE -->|推送评估结果| HUBS
```

**中间件管线顺序**：
```
Request → ExceptionHandling → Serilog RequestLogging
  → SetupGuard → IpWhitelist → JwtAuth → CasbinAuthorization → AuditLog
    → Routing → Authorization → Controller → Response
```

**实际实现的中间件清单**：

| 中间件 | 职责 | 说明 |
|--------|------|------|
| `ExceptionHandlingMiddleware` | 全局异常捕获，统一 JSON 错误响应 | 500 → 带 traceId 的标准化错误体 |
| `SetupGuardMiddleware` | 初始化守卫 | 未初始化时仅放行 `/api/setup` 和 `/health` |
| `IpWhitelistMiddleware` | IP 白名单验证 | 从 `SystemConfig.security.ip_whitelist` 读取白名单列表，支持 CIDR 格式 |
| `JwtAuthMiddleware` | JWT 令牌验证 | 通过 `app.UseAuthentication()` 中间件 |
| `CasbinAuthorizationMiddleware` | RBAC 角色鉴权 | 路由白名单：`/api/auth/*`, `/health`, `/api/setup`, `/hubs/*` 跳过检查 |
| `AuditLogMiddleware` | 审计日志自动记录 | 自动忽略 GET/HEAD/OPTIONS 无变更操作 |

**SignalR Hub — TradingHub 事件合约**：

> **命名规范**：SignalR 事件名采用 PascalCase，如 `PositionUpdated`。审计日志 Action 采用 `resource.operation`（点号分隔）格式，如 `user.login`。

| 事件名 | 推送方向 | Payload 字段 | 触发时机 |
|--------|---------|-------------|---------|
| `PositionUpdated` | Server → Client | `positionId, traderId, exchangeId, strategyId, symbolId, quantity, entryPrice, unrealizedPnl, realizedPnl, status, updatedAtUtc` | 持仓更新/关闭 |
| `OrderPlaced` | Server → Client | `orderId, traderId, exchangeId, strategyId, symbolId, side, type, quantity, status, placedAtUtc` | 下单/成交/取消 |
| `StrategyStatusChanged` | Server → Client | `strategyId, traderId, oldStatus, newStatus, reason, changedAtUtc` | 状态机转换 |
| `RiskAlert` | Server → Client | `alertId, level (Info/Warning/Critical), category (DailyLoss/Drawdown/CircuitBreaker/Cooldown/Slippage/MaxPosition), traderId, strategyId, message, detailJson, triggeredAtUtc` | 风控链拦截 |
| `DashboardSummary` | Server → Client | `totalPnl, totalPositions, activeStrategies, dailyPnl, winRate, lastUpdateAtUtc` | 定时推送 (15s) |
| `ExchangeConnectionChanged` | Server → Client | `exchangeId, traderId, oldStatus (Connected/Disconnected/Error), newStatus, errorMessage, changedAtUtc` | WS 断线/重连 |

#### 3.3.2 TradeX.Trading 组件

```mermaid
flowchart TB
  classDef container fill:#438DD5,color:#fff,stroke:#3377BB,stroke-width:2px
  classDef component fill:#fff,color:#333,stroke:#438DD5,stroke-width:1px

  subgraph TRADING["TradeX.Trading 核心逻辑"]
    ENGINE[TradingEngine<br/>BackgroundService 评估循环]:::component
    EVENTBUS[SignalREventBus<br/>ITradingEventBus<br/>事件广播]:::component
    EXECUTOR[TradeExecutor<br/>ITradeExecutor]:::component
    RISK[RiskCheckHandler<br/>Chain of Responsibility]:::component
    PORTO_RISK[PortfolioRiskManager<br/>IPortfolioRiskManager]:::component
    CONDITION[ConditionTreeEvaluator<br/>IConditionTreeEvaluator]:::component
    BACKTEST[BacktestService<br/>IBacktestService]:::component
    BT_Q[BacktestTaskQueue<br/>Channel&lt;Guid&gt;<br/>多消费者]:::component
    BT_SCHED[BacktestScheduler<br/>多 Worker 并发调度]:::component
    RES_MON[ResourceMonitor<br/>内存+CPU 水位监控<br/>5s 间隔]:::component
    STORE[TaskAnalysisStore<br/>分析数据缓存<br/>Channel+List]:::component
    RECONCILER[OrderReconciler<br/>IOrderReconciler]:::component
  end

  ENGINE -->|事件广播| EVENTBUS
  ENGINE -->|评估入场/出场条件| CONDITION
  ENGINE -->|风控检查| RISK
  ENGINE -->|多层级风控叠加| PORTO_RISK
  ENGINE -->|执行下单| EXECUTOR
  ENGINE -->|启动回测| BACKTEST
  ENGINE -->|启动时同步| RECONCILER

  BACKTEST -->|入队| BT_Q
  BT_Q --> BT_SCHED
  BT_SCHED -->|写入分析数据| STORE
  RES_MON -->|动态控制并发| BT_SCHED

  PORTO_RISK -->|前置检查| RISK
  RISK -->|"通过 -> 下单"| EXECUTOR
```

**风控链节点顺序** (Chain of Responsibility)：

```
入场信号
  → [1] SlippageCheck        — 预估滑点 > 容差？
  → [2] DailyLossCheck       — 当日亏损超限？
  → [3] MaxDrawdownCheck     — 最大回撤超限？
  → [4] ConsecutiveLossCheck — 连续亏损超限？
  → [5] FreqCircuitBreaker   — 5分钟内触发 ≥3 次？
  → [6] CooldownCheck        — 距上次交易 < 冷却期？
  → [7] MaxPositionCheck     — 持仓数已达上限？
  → [通过] ExecuteBuy / ExecuteSell
```

**Volatility Grid 决策流程（新增）**：

```mermaid
flowchart TD
  A[读取当前K线与持仓上下文] --> B{RANGE_PCT >= 阈值?}
  B -- 否 --> Z[不触发首单]
  B -- 是 --> C{是否已有持仓?}
  C -- 否 --> D[按 basePositionSize 首次买入]
  C -- 是 --> E{价格 <= 均价*(1-1%)?}
  E -- 是 --> F{追加次数 < 上限?}
  F -- 是 --> G[执行加仓]
  F -- 否 --> H[禁止继续加仓]
  E -- 否 --> I{价格 >= 均价*(1+1%)?}
  I -- 是 --> J[执行减仓]
  I -- 否 --> K[保持持仓]
```

实现约束：

- 回测引擎与实盘引擎共用同一决策函数，避免行为漂移
- `noStopLoss=true` 仅影响仓位级止损分支，不跳过账户级风险控制
- 双周期（5m/15m）通过双部署实现，执行层增加去重窗口避免重复下单

**回测引擎设计**：

回测以 `BacktestTask` 为单元，由用户发起（每次回测 = 一个异步任务）。

| 方面 | 设计决策 |
|------|---------|
| **回放模式** | 逐 K 线回放（Tick 级回放对现货意义不大）。从 SQLite/IoTDB 读取历史 K 线，以 Close Time 为时间轴步进 |
| **策略执行** | 同一个 `ConditionTreeEvaluator` + `TradeExecutor`（mock 模式），每次 K 线闭合时评估入场/出场条件。风控链仅在回测统计中标记，不拦截 |
| **费用模型** | 可配置：maker/taker 费率（从 `ExchangeSymbolRuleSnapshot` 读取），默认 taker 0.1%。滑点模拟：按成交量百分比线性滑点（默认 0.05%） |
| **绩效指标** | 总收益率、年化收益率、最大回撤 (MDD)、夏普比率、胜率、盈亏比、总交易次数、平均持仓时间 |
| **回测限制** | ① 不支持市价单模拟（以 close 价成交）② 不支持滑点动态模型 ③ 假设无限流动性（订单全部立即成交） |

Volatility Grid 回测补充：

- 使用与实盘一致的均价更新、加减仓触发和追加次数计数逻辑
- 在结果中记录 `pyramidingCount`、每次加减仓触发价格与均价

**绩效指标计算方式**：

| 指标 | 公式 | 说明 |
|------|------|------|
| 总收益率 | `(finalEquity - initialEquity) / initialEquity` | 不含手续费 |
| 年化收益率 | `总收益率 * (365 / 回测天数)` | 简化年化，非复利 |
| 最大回撤 | `max(peak - trough) / peak` | 净值曲线峰值到后续谷值最大跌幅 |
| 夏普比率 | `(年化收益率 - 无风险利率) / 年化波动率` | 无风险利率默认 2% |
| 胜率 | `盈利交易次数 / 总交易次数` | 含手续费后计算盈亏 |
| 盈亏比 | `平均盈利额 / |平均亏损额|` | 含手续费 |

#### 3.3.3 TradeX.Exchange 组件

```mermaid
flowchart TB
  classDef container fill:#438DD5,color:#fff,stroke:#3377BB,stroke-width:2px
  classDef component fill:#fff,color:#333,stroke:#438DD5,stroke-width:1px

  subgraph EXCH["TradeX.Exchange 交易所抽象层"]
    FACTORY[ExchangeClientFactory<br/>运行时创建交易所实例]:::component
    LIMITER[ExchangeRateLimiter<br/>IExchangeRateLimiter<br/>Token Bucket]:::component
    WS_MGR[WebSocketManager<br/>IWebSocketManager<br/>连接/心跳/重连/补发]:::component

    BINANCE_CLI[BinanceClient<br/>IExchangeClient<br/>Binance.Net SDK]:::component
    OKX_CLI[OkxClient<br/>IExchangeClient<br/>JK.OKX.Net SDK]:::component
    GATE_CLI[GateIoClient<br/>IExchangeClient<br/>GateIo.Net SDK]:::component
    BYBIT_CLI[BybitClient<br/>IExchangeClient<br/>Bybit.Net SDK]:::component
    HTX_CLI[HtxClient<br/>IExchangeClient<br/>JKorf.HTX.Net SDK]:::component
  end

  LIMITER -->|限流后请求| FACTORY
  WS_MGR -->|WS 数据流| FACTORY

  FACTORY --> BINANCE_CLI
  FACTORY --> OKX_CLI
  FACTORY --> GATE_CLI
  FACTORY --> BYBIT_CLI
  FACTORY --> HTX_CLI
```

**IExchangeClient 统一接口**：

```csharp
public interface IExchangeClient
{
    // Market Data
    IAsyncEnumerable<Candle> SubscribeKlinesAsync(string symbol, string interval, CancellationToken ct);
    Task<Candle[]> GetKlinesAsync(string symbol, string interval, DateTime start, DateTime end, CancellationToken ct);
    Task<OrderBook> GetOrderBookAsync(string symbol, int limit, CancellationToken ct);

    // Account
    Task<AccountBalance> GetBalanceAsync(CancellationToken ct);
    Task<Position[]> GetPositionsAsync(CancellationToken ct);

    // Trading
    Task<OrderResult> PlaceOrderAsync(OrderRequest request, CancellationToken ct);
    Task<OrderResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct);
    Task<OrderResult> GetOrderAsync(string exchangeOrderId, CancellationToken ct);
    Task<OrderResult[]> GetRecentOrdersAsync(DateTime since, CancellationToken ct);

    // Validation
    Task<ConnectionTestResult> TestConnectionAsync(CancellationToken ct);

    // Rules
    Task<SymbolRule[]> GetSymbolRulesAsync(CancellationToken ct);
}
```

#### 3.3.4 TradeX.Infrastructure 组件

```mermaid
flowchart TB
  classDef container fill:#438DD5,color:#fff,stroke:#3377BB,stroke-width:2px
  classDef component fill:#fff,color:#333,stroke:#438DD5,stroke-width:1px

  subgraph INFRA["TradeX.Infrastructure"]
    DBCONTEXT[TradeXDbContext<br/>EF Core DbContext]:::component
    REPOS[Repositories<br/>仓储模式]:::component
    CASBIN[CasbinEnforcer<br/>Casbin.NET Enforcer]:::component
    IOTDB_CLIENT[IoTDbClient<br/>时序数据访问<br/>K 线写入与查询]:::component
    CRYPTO[CryptoService<br/>AES-256-GCM<br/>API Key / MFA Secret 加密]:::component
    HASHER[PasswordHasher<br/>bcrypt<br/>密码哈希]:::component
  end

  DBCONTEXT -->|LINQ 查询| REPOS
  CASBIN -.->|策略持久化（可选）| REPOS
```

#### 3.3.5 TradeX.Notifications 组件

```mermaid
flowchart TB
  classDef container fill:#438DD5,color:#fff,stroke:#3377BB,stroke-width:2px
  classDef component fill:#fff,color:#333,stroke:#438DD5,stroke-width:1px
  classDef ext fill:#686868,color:#fff,stroke:#555,stroke-width:2px,stroke-dasharray:5 5

  subgraph NOTIF["TradeX.Notifications"]
    SERVICE[NotificationService<br/>INotificationService<br/>事件路由 + 模板渲染 + 渠道分发]:::component
    TEMPLATES[NotificationTemplates<br/>静态模板定义<br/>交易/异常/风控]:::component
  end

  TG[TelegramSender<br/>Bot API<br/>HTTP POST]:::ext
  DC[DiscordSender<br/>Webhook<br/>HTTP POST]:::ext
  EM[EmailSender<br/>SMTP<br/>MailKit]:::ext
  REPOS_INFRA[Repositories<br/>渠道配置读取]:::ext

  SERVICE -->|发送 Telegram| TG
  SERVICE -->|发送 Discord| DC
  SERVICE -->|发送 Email| EM
  SERVICE -->|渲染模板| TEMPLATES
  SERVICE -->|读取渠道配置| REPOS_INFRA
```

#### 3.3.6 TradeX.Core 领域模型

Core 是 TradeX 的领域叶节点，包含枚举定义、值对象和领域接口。**零依赖**。

**核心枚举**：

| 枚举 | 值 | 使用场景 |
|------|-----|---------|
| `ExchangeType` | `Binance`, `Okx`, `GateIo`, `Bybit`, `Htx` | Exchange 配置、客户端工厂 |
| `StrategyStatus` | `Draft`, `Backtesting`, `Passed`, `Active`, `Disabled` | 策略状态机 |
| `OrderSide` | `Buy`, `Sell` | 订单、持仓方向 |
| `OrderType` | `Market`, `Limit` | 订单类型（当前仅实现 Market） |
| `OrderStatus` | `Pending`, `PartiallyFilled`, `Filled`, `Cancelled`, `Failed` | 订单生命周期 |
| `PositionStatus` | `Open`, `Closed` | 持仓状态 |
| `UserRole` | `SuperAdmin`, `Admin`, `Operator`, `Viewer` | RBAC 鉴权 |
| `UserStatus` | `PendingMfa`, `Active`, `Disabled` | 用户状态 |
| `TraderStatus` | `Active`, `Disabled` | 交易员状态 |
| `RiskAlertLevel` | `Info`, `Warning`, `Critical` | 风控告警级别 |
| `RiskAlertCategory` | `DailyLoss`, `MaxDrawdown`, `ConsecutiveLoss`, `CircuitBreaker`, `Cooldown`, `Slippage`, `MaxPosition` | 风控类别 |
| `ConnectionStatus` | `Connected`, `Disconnected`, `Error` | 交易所连接状态 |
| `BacktestTaskStatus` | `Pending`, `Running`, `Completed`, `Failed`, `Cancelled` | 回测任务状态 |
| `NotificationChannelType` | `Telegram`, `Discord`, `Email` | 通知渠道类型 |

**核心值对象**：

| 值对象 | 字段 | 用途 |
|--------|------|------|
| `Symbol` | `ExchangeType + BaseAsset + QuoteAsset` (e.g. `Binance:BTCUSDT`) | 交易对唯一标识 |
| `Money` | `decimal Amount + string Currency` 或 `string Asset` | 金额/资产数量 |
| `Candle` | `Open/High/Low/Close/Volume/Timestamp` | K 线数据 |
| `KlineInterval` | 标准间隔值对象 (`1m`, `5m`, `15m`, `1h`, `4h`, `1d`) | K 线时间间隔 |

#### 3.3.7 TradeX.Indicators 组件

```mermaid
flowchart TB
  classDef component fill:#fff,color:#333,stroke:#438DD5,stroke-width:1px

  subgraph INDIC["TradeX.Indicators"]
    CALC[IndicatorCalculator<br/>IIndicatorCalculator<br/>Skender 封装层]:::component
    REG[IndicatorRegistry<br/>指标注册表<br/>名称 -> 指标映射]:::component

    SMA[SimpleMovingAverage<br/>简单移动平均]:::component
    EMA[ExponentialMovingAverage<br/>指数移动平均]:::component
    MACD[MACD<br/>指数平滑异同平均]:::component
    RSI[RSI<br/>相对强弱指数]:::component
    BB[BollingerBands<br/>布林带]:::component
    STOCH[Stochastic<br/>随机指标 KDJ]:::component
    ATR[ATR<br/>平均真实波幅]:::component
    VOL[Volume<br/>成交量分析]:::component
  end

  CALC --> REG
  REG --> SMA
  REG --> EMA
  REG --> MACD
  REG --> RSI
  REG --> BB
  REG --> STOCH
  REG --> ATR
  REG --> VOL
```

**接口定义**：

```csharp
public interface IIndicatorCalculator<TResult>
    where TResult : class
{
    string Name { get; }
    TResult Calculate(IReadOnlyList<Candle> candles);
    bool RequiresWarmup { get; }
    int WarmupPeriod { get; }
}
```

**首批支持的 8 个指标**：

| 指标 | 计算方式 | Warmup | 关键参数 | 典型使用 |
|------|---------|--------|---------|---------|
| SMA | 简单移动平均 | period - 1 | period: 5-200 | 趋势识别 |
| EMA | 指数加权移动平均 | period × 2 | period: 5-200 | 快线/慢线交叉 |
| MACD | EMA(12) - EMA(26) + Signal(9) | 33 | fast/slow/signal | 趋势动量 |
| RSI | 平均涨幅 / 平均跌幅 | period + 1 | period: 14 | 超买/超卖 (70/30) |
| BB | 中轨 ± k × 标准差 | period - 1 | period: 20, k: 2 | 波动率/突破 |
| Stochastic | %K = (C-L14)/(H14-L14)×100 | period + 1 | k/d: 5/3 | 超买/超卖 (80/20) |
| ATR | EMA of True Range | period - 1 | period: 14 | 波动率/止损 |
| Volume | 均值对比 | 0 | period: 20 | 放量/缩量 |

---

## 4. 数据架构

### 4.1 数据存储分布策略

| 数据类别 | 存储引擎 | 访问模式 | 一致性要求 | 备份策略 |
|----------|---------|---------|-----------|---------|
| 用户 / 配置 | SQLite | 低频读写 | 强一致 | `sqlite3 .backup` |
| 交易所凭证 | SQLite (AES 加密) | 低频读 | 强一致 | 同上 |
| 策略 / 订单 | SQLite | 中频读写 | 强一致 | 同上 |
| 持仓 | SQLite | 高频更新（15s/cycle） | 最终一致 | 同上 |
| 审计日志 | SQLite | 仅追加写 + 低频范围查 | 最终一致 | 同上 + 归档 |
| K 线历史 | IoTDB | 高频追加写 + 范围读 | 最终一致 | IoTDB snapshot |
| 归档订单 | JSON (gzip) | 极低频读 | 最终一致 | 与 SQLite 同卷 |

### 4.2 ER 图 (核心实体)

```mermaid
erDiagram
  User ||--o{ Trader : creates
  User ||--o{ AuditLog : generates
  User ||--o{ RefreshToken : has

  Trader ||--o{ Exchange : owns
  Trader ||--o{ StrategyDeployment : deploys
  Trader ||--o{ Position : owns
  Trader ||--o{ Order : owns

  Exchange ||--o{ StrategyDeployment : targets
  Exchange ||--o{ Symbol : lists
  Exchange ||--o{ Position : holds
  Exchange ||--o{ Order : executes

  Strategy ||--o{ StrategyDeployment : template_for
  StrategyDeployment ||--o{ BacktestTask : tested_by
  StrategyDeployment ||--o{ Position : opens

  BacktestTask ||--|| BacktestResult : produces

  Symbol ||--o{ StrategyDeployment : references
  Symbol ||--o{ Position : trades
  Symbol ||--o{ Order : trades

  User {
    string Id PK
    string UserName
    string PasswordHash
    string Role
    string Status
    string MfaSecretEncrypted
    string RecoveryCodesJson
  }

  Trader {
    string Id PK
    string Name
    string Status
    string CreatedBy FK
  }

  Exchange {
    string Id PK
    string TraderId FK
    string Name
    string Type
    string ApiKeyEncrypted
    string SecretKeyEncrypted
    string PassphraseEncrypted
  }

  Strategy {
    string Id PK
    string Name
    string EntryConditionJson
    string ExitConditionJson
    string ExecutionRuleJson
    int Version
  }

  StrategyDeployment {
    string Id PK
    string StrategyId FK
    string TraderId FK
    string ExchangeId
    string SymbolIds
    string Timeframe
    string Status
  }

  Position {
    string Id PK
    string TraderId FK
    string ExchangeId FK
    string StrategyDeploymentId FK
    string SymbolId
    decimal Quantity
    decimal EntryPrice
    decimal UnrealizedPnl
    decimal RealizedPnl
    string Status
  }

  Order {
    string Id PK
    string TraderId FK
    string ExchangeId FK
    string StrategyDeploymentId FK
    string PositionId
    string SymbolId
    string Side
    string Type
    string Status
    decimal Quantity
    decimal FilledQuantity
    decimal Fee
    bool IsManual
  }

  RefreshToken {
    string Id PK
    string UserId FK
    string Token
    datetime ExpiresAt
    datetime RevokedAt
  }

  AuditLog {
    string Id PK
    string UserId
    string Action
    string ResourceType
    string ResourceId
    string DetailJson
    string RequestIp
  }
```

### 4.3 IoTDB 时序数据设计

| 序列路径 | 数据类型 | 说明 |
|----------|---------|------|
| `root.tradex.{exchange}.{symbol}.kline.{interval}.open` | DOUBLE | 开盘价 |
| `root.tradex.{exchange}.{symbol}.kline.{interval}.high` | DOUBLE | 最高价 |
| `root.tradex.{exchange}.{symbol}.kline.{interval}.low` | DOUBLE | 最低价 |
| `root.tradex.{exchange}.{symbol}.kline.{interval}.close` | DOUBLE | 收盘价 |
| `root.tradex.{exchange}.{symbol}.kline.{interval}.volume` | DOUBLE | 成交量 |
| `root.tradex.{exchange}.{symbol}.kline.{interval}.closeTime` | INT64 | 收盘时间戳 |

**存储组设计**: `root.tradex` 单存储组（单机部署，无需跨组）

**保留策略**: 90 天滚动删除（通过 IoTDB 的 TTL 设置）

---

## 5. 数据流架构

### 5.1 实时交易数据流

```mermaid
flowchart LR
  subgraph 交易所侧
    WS[交易所 WebSocket]
  end

  subgraph 内存层
    MC[(Memory Cache<br/>ConcurrentDict)]
    IC[Indicator Calculator<br/>Skender]
  end

  subgraph 策略层
    CT[Condition Tree Evaluator]
    RC[Risk Chain of Resp.]
    PRC[Portfolio Risk Manager]
  end

  subgraph 执行层
    EX[Trade Executor]
    DB[(SQLite / IoTDB)]
    SR[SignalR Push]
    NF[Notification]
  end

  WS -- "kline/ticker" --> MC
  MC --> IC
  IC --> CT
  CT -- "入场条件满足" --> RC
  RC -- "单策略通过" --> PRC
  PRC -- "四层风控通过" --> EX
  EX -- "下单" --> DB
  EX -- "推送" --> SR
  EX -- "告警" --> NF

  style WS fill:#f96
  style RC fill:#f9c
  style PRC fill:#f9c
```

### 5.2 系统启动数据流

```mermaid
flowchart TB
  START([应用启动]) --> MIG[EF Core Migrations]
  MIG --> CFG[加载配置 + Casbin]
  CFG --> INIT{Super Admin 存在？}

  INIT -- 否 --> MODE_INIT[初始化模式：仅开放 /health /api/setup/*]
  INIT -- 是 --> MODE_NORMAL[正常模式]

  MODE_NORMAL --> RECON[Order Reconciliation<br/>对每个已启用 Exchange]
  RECON --> WS_CONNECT[建立 WebSocket 连接<br/>+K 线预热]
  WS_CONNECT --> LOOP[启动策略评估循环<br/>15s/cycle]
  LOOP --> READY[Health 端点就绪 ✅]

  RECON --> FAIL{同步失败？}
  FAIL -- 是 --> LOG_WARN[记录告警，不阻塞启动]
  FAIL -- 否 --> WS_CONNECT
```

### 5.3 WebSocket 断线重连 + K 线回填

```mermaid
sequenceDiagram
  participant WS as WebSocket Manager
  participant MC as Memory Cache
  participant REST as Exchange REST API
  participant IoTDB as IoTDB

  WS->>WS: 检测断线
  WS->>WS: 指数退避重连<br/>1s → 2s → 4s → ... → 30s max
  WS->>WS: 重连成功

  WS->>REST: GET /klines<br/>since={lastKnownTimestamp}
  REST-->>WS: 缺失区间 K 线数据

  WS->>MC: 写入内存缓存
  WS->>IoTDB: 写入时序数据库（去重）
  WS->>WS: 恢复实时 WS 订阅
```

### 5.4 条件树数据结构 (Condition Tree JSON Schema)

策略的入场/出场条件以 JSON 树形式存储在 `Strategy.EntryConditionJson` / `ExitConditionJson` 字段中。

**节点类型定义**：

| 节点类型 | 作用 | 子节点 | 示例 |
|---------|------|--------|------|
| `And` | 所有子条件同时满足 | N 个条件节点 | `{ type: "And", conditions: [...] }` |
| `Or` | 任一子条件满足 | N 个条件节点 | `{ type: "Or", conditions: [...] }` |
| `Not` | 取反子条件 | 1 个条件节点 | `{ type: "Not", condition: ... }` |
| `Indicator` | 指标值比较 | 0（叶节点） | `{ type: "Indicator", ... }` |

**Indicator 叶节点结构**：

```json
{
  "type": "Indicator",
  "indicator": "RSI",
  "parameters": { "period": 14 },
  "source": "1h",
  "operator": "LessThan",
  "value": 30,
  "offset": 0
}
```

| 字段 | 说明 | 取值范围 |
|------|------|---------|
| `indicator` | 指标名称 | `SMA`, `EMA`, `MACD`, `RSI`, `BB`, `Stochastic`, `ATR`, `Volume` |
| `parameters` | 指标参数 | 各指标对应的参数 (period/fast/slow/signal/k 等) |
| `source` | K 线周期 | `15m`, `1h`, `4h`, `1d` |
| `operator` | 比较运算符 | `GreaterThan`, `LessThan`, `CrossAbove`, `CrossBelow`, `Inside`, `Outside` |
| `value` | 比较值或阈值 | 数值或 JSON 对象（BB 上下轨等） |
| `offset` | K 线偏移量 | `0` 当前未闭合 K 线, `1` 上一根已闭合 |

**完整条件树示例（入场条件）**：

```json
{
  "type": "And",
  "conditions": [
    {
      "type": "Indicator",
      "indicator": "RSI",
      "parameters": { "period": 14 },
      "source": "1h",
      "operator": "CrossAbove",
      "value": 30,
      "offset": 1
    },
    {
      "type": "Indicator",
      "indicator": "EMA",
      "parameters": { "period": 50 },
      "source": "4h",
      "operator": "GreaterThan",
      "value": {
        "type": "Indicator",
        "indicator": "EMA",
        "parameters": { "period": 200 },
        "source": "4h",
        "operator": null,
        "value": null,
        "offset": 1
      },
      "offset": 1
    }
  ]
}
```

**约束规则**：

| # | 规则 | 校验逻辑 |
|---|------|---------|
| 1 | 树深度 ≤ 5 层 | 递归遍历，超限拒绝 |
| 2 | Not 节点必须且仅有 1 个子节点 | 检查 children.length == 1 |
| 3 | Indicator 节点必须为叶节点 | 检查 hasChildren == false |
| 4 | CrossAbove / CrossBelow 不能用于多指标对比 | 仅支持与常数值比较 |
| 5 | source 必须为配置中已启用的 K 线周期 | 校验枚举值 |
| 6 | offset 仅 0/1，出场条件允许 -1（下一周期） | 校验整数值范围 |

---

## 6. 架构决策记录 (ADR)

### ADR-001: SQLite 为主存储

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | TradeX 为单机 Docker 部署系统，数据规模可控（配置数据 < 1GB/年） |
| **决策** | 使用 SQLite 作为主存储引擎，EF Core 作为 ORM |
| **理由** | 零运维、零独立进程、备份简单 (`sqlite3 .backup`)、EF Core 迁移成熟 |
| **后果** | 不支持多实例并发写；写锁争用需注意（Trading Engine 15s/cycle 写入 Order）；归档机制可缓解单表增长 |
| **替代否决** | PostgreSQL（增加运维复杂度，单机场景过重） |

### ADR-002: IoTDB 为时序数据库

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | K 线数据是典型的时序数据：高频追加写、范围读、无需事务 |
| **决策** | 使用 Apache IoTDB 1.3.3 存储历史 K 线 |
| **理由** | 原生支持时间序列（优于 SQLite）、列式压缩存储空间、内置 TTL 过期、Docker 化部署简单 |
| **后果** | 引入额外容器依赖；初期可 SQLite 降级（FR-13 K 线预热可直接从交易所 REST 拉取） |
| **替代否决** | InfluxDB（Docker 镜像 > 300MB vs IoTDB ~200MB）；TimescaleDB（依赖 PostgreSQL） |

### ADR-003: Casbin.NET 为鉴权引擎

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | 四角色（SuperAdmin / Admin / Operator / Viewer）基于 API 路径 + HTTP 方法的 RBAC |
| **决策** | 使用 Casbin.NET，模型为 `keyMatch3 + regexMatch` |
| **理由** | 策略与代码分离（model.conf + policy.csv）、支持热加载（不用重启）、C# 生态成熟 |
| **后果** | 每新增 API 端点需同步更新 policy.csv；需要额外 CasbinAuthorizationMiddleware |
| **替代否决** | 手写 middleware + Attribute（策略硬编码，变更需重编译）；`[Authorize(Roles="...")]`（不支持路径模式匹配） |

### ADR-004: Chain of Responsibility 为风控模式

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | 7 个风控检查项，未来可能增加；检查顺序固定，每个独立可配置 |
| **决策** | 使用 Chain of Responsibility 模式组装风控管线 |
| **理由** | 新增检查项只需新加 Handler 并注册；每个检查独立测试；管线顺序一目了然 |
| **后果** | 需注意 Handler 之间不要产生隐含状态依赖；通过 RiskContext 传递共享数据 |
| **替代否决** | 硬编码 if-else 链（违反开闭原则）；装饰器模式（不便于管线控制） |

### ADR-005: Trading Engine 为单实例 BackgroundService

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | 策略评估循环需要维护内存态 K 线缓存和活跃策略列表 |
| **决策** | TradingEngine 作为 `BackgroundService` 在 API 进程中运行，固定评估周期 15s |
| **理由** | 避免部署分布式缓存（Redis）的复杂度；单机场景可接受 15s 延迟；直接访问内存缓存无网络开销 |
| **后果** | 无法水平扩展多实例（多实例会导致重复下单）。如未来需要 HA，需引入分布式锁 + Redis 缓存 |
| **替代否决** | 独立 Trading Worker 进程（增加部署和 IPC 复杂度，不合算） |

### ADR-006: JKorf 系列为交易所 SDK

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | 5 个交易所需要统一的 .NET SDK 封装 |
| **决策** | 使用 JKorf 系列各交易所 SDK（Binance.Net / JK.OKX.Net / GateIo.Net / Bybit.Net / JKorf.HTX.Net） |
| **理由** | 统一设计风格、社区活跃、WebSocket 内置、支持 .NET 10；网帘层 `IExchangeClient` 屏蔽 SDK 差异 |
| **后果** | SDK 本身有第三方依赖风险；需关注各 SDK 的 net10.0 兼容性 |
| **替代否决** | 各交易所官方 SDK（API 风格差异大、封装成本高、部分无官方 .NET SDK） |

### ADR-007: Skender.Stock.Indicators 为指标库

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | 技术支持 8 个首批指标，未来可扩展 |
| **决策** | 封装 Skender.Stock.Indicators 为 `TradeX.Indicators` 模块 |
| **理由** | 50+ 指标、活跃维护、net10.0 兼容；封装层可替换底层库 |
| **后果** | 需确保计算结果与交易所内置指标一致（尤其 KDJ 等非标准指标） |
| **替代否决** | Trady（已不活跃）；TA-Lib（C 依赖，跨平台问题） |

### ADR-008: AES-256-GCM 为凭证加密算法

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | API Key / Secret / Passphrase / MFA Secret 需加密存储 |
| **决策** | 使用 AES-256-GCM（认证加密） |
| **理由** | GCM 模式内置认证标签（防篡改）、NIST 推荐、.NET `AesGcm` 类原生支持 |
| **后果** | 密钥管理在 `jwt.secret` 派生（或独立密钥），需密钥轮换策略；密钥丢失 = 数据不可恢复 |
| **替代否决** | AES-CBC + HMAC（需要手动组合，易出错）；RSA（性能差，不适合大批量加解密） |

### ADR-009: JSON 字段命名统一为小驼峰 (camelCase)

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | 前后端通过 HTTP JSON 通信，后端 `Ok(new { ... })` 匿名对象默认 C# PascalCase，前端接口字段定义风格不统一导致多次修复（camelCase → snake_case → camelCase） |
| **决策** | 全系统 JSON 序列化统一使用 camelCase（小驼峰） |
| **理由** | (1) camelCase 是 JSON/Web API 社区标准（JavaScript/TypeScript/Blazor 均采用），前端可直接使用 `response.dataField` 无需映射； (2) ASP.NET Core `AddJsonOptions(o => o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase)` 一行配置即可全局生效； (3) 匿名对象和 DTO 均自动转换，无需逐个加 `[JsonPropertyName]` |
| **实施方式** | (1) 后端 `Program.cs` 中 `AddJsonOptions` 配置 `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`； (2) 手动 `JsonSerializer.Serialize` 处（如 `BacktestEngine.cs`）显式传入相同 options； (3) 前端 API 接口定义使用 camelCase 字段名； (4) Blazor 组件中引用 API 字段时直接使用 camelCase |
| **例外** | Controller 查询参数（`[FromQuery]`）不受影响，仍可按 C# 命名 |
| **后果** | 所有 HTTP JSON 响应字段均为小驼峰；新增 API 端点时前后端字段命名需保持一致 |
| **替代否决** | PascalCase（C# 默认，前端需要额外映射层）；snake_case（Python 友好但 JSON 社区不占优势） |

### ADR-010: SSE 为回测 K 线实时推送协议

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | 回测执行时前端需实时查看 K 线分析过程，完成后支持回放；SignalR 已集成但 SSE 更轻量 |
| **决策** | 使用 SSE（Server-Sent Events）作为回测分析数据的实时推送协议，已完成任务通过 REST + 客户端定时器实现回放 |
| **理由** | (1) SSE 基于 HTTP 长连接，天然携带 `Authorization` 头（对比 EventSource 无法自定义请求头）；(2) 只需后端写入，无需双向通信，SSE 比 SignalR 更轻量（无额外帧协议）；(3) `fetch + ReadableStream` 解析 SSE 行协议即可兼容 JWT 认证；(4) 已完成任务的回放不依赖 SSE，使用 REST 全量拉取后客户端逐根显示，架构更简单 |
| **实施方式** | (1) `TaskAnalysisStore` 为每个任务维护 `List<T> + Channel<T>`，`Push` 时同时写入；(2) SSE 端点 `GET /tasks/{id}/analysis/stream` 运行中走 Channel，已完成走 DB 反序列化后 `Task.Delay` 控制速度；(3) 前端 `fetch + ReadableStream` 解析 `data: {...}\n\n` 格式；(4) 运行时增量推送，已完成全量拉取后客户端 `setInterval` 自增 index |
| **后果** | SSE 断连需前端重试；Channel 在 Worker 完成后 `TryComplete()` |
| **替代否决** | SignalR（已集成但推荐 SSE）；WebSocket（过度设计）；轮询（浪费带宽、延迟大） |

### ADR-011: 回测并发调度采用资源感知调度

| 属性 | 值 |
|------|-----|
| **状态** | **Accepted** |
| **上下文** | 默认 `BacktestWorker` 为单消费者（`Channel.SingleReader = true`），逐任务串行处理。随着回测使用增加，单线程成为吞吐瓶颈。但无限制并发会挤压 `TradingEngine` 的 15s 评估周期资源，且有 OOM 风险（大数据量回测峰值接近 1GB 内存） |
| **决策** | 使用资源感知的并发调度：固定 Worker 池 + `ResourceMonitor` 根据内存 + CPU 水位动态控制活跃并发数 |
| **理由** | (1) 固定并发上限（硬编码 `MaxConcurrency`）在资源空闲时浪费吞吐，在资源紧张时仍可能压垮系统——动态调度能自适应；(2) 回测是 CPU + IO + 内存密集型操作，`TradingEngine` 每 15 秒跑一次实时策略评估，回测并发必须让路；(3) 联合监控内存（`GC.GetTotalMemory()`）和 CPU（`Process.TotalProcessorTime` 间隔采样），`AllowedConcurrency = min(mem_cap, cpu_cap)`，任一资源紧张即自动降级 |
| **实施方式** | (1) `BacktestTaskQueue.Channel` 改为 `SingleReader = false`，允许多消费者；(2) 新增 `IResourceProvider` 接口（`GetMemoryMb()`/`GetCpuPercent()`），`SystemResourceProvider` 调 `GC.GetTotalMemory()` + `Process.TotalProcessorTime`，另提供 Mock 实现供测试使用；(3) 新增 `ResourceMonitor`（Singleton + `IHostedService`），注入 `IResourceProvider`，每 5 秒采样内存 + CPU，按双维度四档水位计算 `AllowedConcurrency = min(mem_cap, cpu_cap)`，首个 CPU 采样周期返回 `MaxConcurrency`（无有效差值前不限制）；(4) `BacktestScheduler` 启动 `MaxConcurrency` 个 Worker 任务，每个循环 `TryAcquire()` → `ReadAsync()` → `ProcessTaskAsync()` → `Release()`；(5) acquire/release 使用 `lock` 保护计数器，未获取到槽位时 200ms 轮询重试；(6) 配置走 `IOptions<BacktestSchedulerSettings>`（含内存阈值 + CPU 阈值）；(7) `GET /health` 暴露 `runningCount`、`allowedConcurrency`、`currentMemoryMb`、`currentCpuPercent`（`HealthController` 注入 `ResourceMonitor`）；(8) 启动时 `RecoverStuckTasksAsync` 将 N 个 Running 任务全部重置为 Pending |
| **后果** | Worker 池大小固定为 `MaxConcurrency`，不会无限增长；进行中的回测不会被中断，完成后才受新水位影响；Channel 多消费者公平竞争；水位下降时 200ms 自动恢复；`TaskAnalysisStore` 累积内存受 `MaxConcurrency` 天然约束；SSE Remove() 竞态为良性（前端已完成状态切换） |
| **替代否决** | `SemaphoreSlim` 固定并发（静态上限无法自适应）；无限制并发（内存溢出风险）；独立回测进程（IPC + 部署复杂度，不合算） |

---

## 7. 部署架构

### 7.1 Docker Compose 网络拓扑

```mermaid
flowchart TB
  classDef external fill:#f5f5f5,color:#333,stroke:#999,stroke-width:2px
  classDef group fill:none,stroke:#666,stroke-width:2px,stroke-dasharray:5 5
  classDef volume fill:#e8f4e8,color:#333,stroke:#4caf50,stroke-width:1px

  subgraph External["External"]
    direction TB
    USER[("用户浏览器 :80")]:::external
    EXS[("交易所 :443")]:::external
  end

  subgraph NETWORK["tradex-network (bridge)"]
    subgraph APP["tradex — 统一容器"]
      API[ASP.NET Core 10<br/>REST + SPA + SignalR + BackgroundService<br/>:80]
    end

    subgraph TSDB["iotdb"]
      IOTDB[IoTDB :6667 :8181]
    end

    VOL_DATA[(tradex-data)]:::volume
    VOL_IOTDB_DATA[(iotdb-data)]:::volume
    VOL_IOTDB_WAL[(iotdb-wal)]:::volume
  end

  USER --> API
  API --> VOL_DATA
  API --> IOTDB
  API --> EXS

  IOTDB --> VOL_IOTDB_DATA
  IOTDB --> VOL_IOTDB_WAL
```

### 7.2 Blazor Server 路由配置

Blazor Interactive Server 通过 `MapRazorComponents` 处理所有页面路由，无需独立 Nginx 容器。

```csharp
// Program.cs
app.UseRouting();
app.MapRazorComponents<App>()
   .AddInteractiveServerRenderMode();
app.MapControllers();
app.MapHub<TradingHub>("/hubs/trading");
app.MapHealthChecks("/health");
```

所有请求流向：
```
/:80
  ├── /_blazor/*     → Blazor Server SignalR 连接
  ├── /api/*         → Controllers
  ├── /hubs/trading  → SignalR Hub
  ├── /health        → Health Check
  └── /*              → Blazor 页面路由
```

### 7.3 环境变量配置清单

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `ASPNETCORE_ENVIRONMENT` | Production | 运行环境 |
| `ASPNETCORE_URLS` | http://+:80 | 监听地址（统一端口） |
| `ConnectionStrings__Sqlite` | Data Source=/data/tradex.db | SQLite 路径 |
| `Jwt__Secret` | — | **必填**，JWT 签名密钥 |
| `Jwt__AccessTokenExpiresMinutes` | 30 | AccessToken 有效期 |
| `Jwt__RefreshTokenExpiresDays` | 7 | RefreshToken 有效期 |
| `IoTDB__Host` | localhost | IoTDB 主机 |
| `IoTDB__Port` | 6667 | IoTDB Thrift 端口 |
| `Serilog__MinimumLevel__Default` | Information | 日志级别 |

---

## 8. 横切关注点

### 8.1 错误处理策略

| 层级 | 策略 | 说明 |
|------|------|------|
| Controller | 全局异常过滤器 `ExceptionHandlingMiddleware` | 500 → 统一 JSON 错误响应 + 日志 + traceId |
| Service/Business | 传播异常，自定义 `DomainException` | 400/404/409 → 带业务错误码 |
| Trading Engine | catch + log + 继续循环 | 单策略异常不影响其他策略；连续 N 次异常暂停该策略 |
| Exchange Client | catch API 异常 → 转换为 `ExchangeException` | 包含错误码、HTTP 状态、原始消息 |
| WebSocket | 断线重连自动恢复 | 重连失败上限后标记 `Disconnected`，不 crash |

### 8.2 日志策略

| 日志类别 | Serilog Sink | 保留策略 | 格式 |
|----------|-------------|---------|------|
| 应用日志 | Console + File | 7 天轮换 | JSON 结构化 |
| 审计日志 | AuditLog 表 | 6 个月 | 结构化 + 已索引 |
| 交易日志 | Console + File | 30 天轮换 | 结构化（含 OrderId/TraderId） |
| 风控日志 | Console + File | 30 天轮换 | 结构化（含 RiskContext） |

### 8.3 并发策略

| 场景 | 策略 | 说明 |
|------|------|------|
| 同一 Symbol 同时触发买入 | 策略级锁 + 风控熔断 | Trading Engine 同周期内同一 Symbol 仅执行一次 |
| 手动下单 vs 策略下单 | 无冲突（共用风控链） | 手动下单同样经过滑点 + 日亏损检查 |
| 策略编辑 vs 策略评估 | 乐观并发（Version 字段） | `UPDATE ... WHERE Version = @expected` |
| 订单状态更新 | 幂等更新 | `Order.Status = Filled` 可重复执行，不报错 |
| 回测多任务同时执行 | 资源感知调度（ResourceMonitor + BacktestScheduler） | Worker 池固定 `MaxConcurrency` 个，`ResourceMonitor` 每 5s 采样内存 + CPU，`AllowedConcurrency = min(mem_cap, cpu_cap)`，按双维度水位动态限制活跃并发数；超过水位的新任务等待槽位释放，不中断进行中的任务 |

### 8.4 安全边界

| 安全域 | 信任边界 | 控制点 |
|--------|---------|--------|
| 浏览器 ↔ Nginx | 不可信 | HTTPS (生产)、CORS、XSS 防护（前端输出编码） |
| Nginx ↔ Backend | 可信内网 | Docker 内网通信 |
| Backend ↔ 交易所 | 不可信 | API Key 仅内存解密；错误处理不泄露凭证 |
| Backend ↔ SQLite | 可信 | 文件权限 0600 |
| Backend ↔ IoTDB | 可信 | Docker 内网通信 |

---

## 9. 质量属性

### 9.1 性能

| 指标 | 目标 | 测量方式 |
|------|------|---------|
| Trading Engine 评估周期 | ≤ 15s | 日志记录 cycle start/end |
| WebSocket 数据延迟 | ≤ 2s（交易所 → 内存缓存） | 时间戳比对 |
| REST API P95 响应时间 | ≤ 500ms | 中间件记录 |
| SignalR 推送延迟 | ≤ 1s | 客户端时间戳 |
| 回测 30 天 15m K 线 | ≤ 10s | 计时 |

### 9.2 可用性

| 场景 | 行为 | RTO | RPO |
|------|------|-----|-----|
| 进程崩溃 | Docker restart + Reconciliation 恢复 | < 30s | 0（SQLite 写后即落盘）|
| IoTDB 不可用 | K 线降级为仅内存缓存 + 不写历史 | N/A | 丢失断线期间 K 线 |
| 交易所 API 5xx | 限流 + 自动暂停（连续 N 次）→ 手动恢复 | < 1min | N/A |
| 交易所 WS 断线 | 指数退避重连 + K 线回填 | < 30s | 断线期间的 K 线通过 REST 回填 |

### 9.3 安全性

| 控制项 | 实现 |
|--------|------|
| 认证 | JWT AccessToken + RefreshToken + MFA TOTP |
| 授权 | Casbin RBAC（API 路径 + HTTP 方法级别） |
| 加密 | AES-256-GCM 凭证加密；bcrypt 密码哈希；HTTPS |
| 审计 | 敏感操作自动记录 AuditLog |
| 防暴力 | MFA 失败次数上限（5 次锁定 5 分钟） |

---

## 10. 关键接口合约速查

| 接口 | 模块 | 用途 |
|------|------|------|
| `IExchangeClient` | TradeX.Exchange | 交易所统一操作抽象 |
| `IWebSocketManager` | TradeX.Exchange | WS 连接生命周期 |
| `IExchangeRateLimiter` | TradeX.Exchange | 交易所 API 限流 |
| `IIndicatorCalculator<TResult>` | TradeX.Indicators | 技术指标计算抽象 |
| `IConditionTreeEvaluator` | TradeX.Trading | 条件树评估 |
| `IRiskCheckHandler` | TradeX.Trading | 风控单节点 |
| `IPortfolioRiskManager` | TradeX.Trading | 四层组合风控 |
| `ITradeExecutor` | TradeX.Trading | 下单执行 |
| `ITradingEngine` | TradeX.Trading | 引擎启停控制 |
| `IOrderReconciler` | TradeX.Trading | 订单同步 |
| `IBacktestService` | TradeX.Trading | 回测服务 |
| `IBacktestTaskQueue` | TradeX.Trading | 回测任务队列 |
| `ITradingEventBus` | TradeX.Api | 交易事件总线 |
| `IResourceProvider` | TradeX.Trading | 系统资源抽象 |
| `IIndicatorService` | TradeX.Indicators | 指标计算服务 |
| `INotificationService` | TradeX.Notifications | 通知发送 |
| `IRepository<T>` | TradeX.Infrastructure | 数据持久化 |

---

## 11. 架构演进路线

| 阶段 | 架构目标 | 关键变化 |
|------|---------|---------|
| M1-M2 | 单体可运行 | SQLite + 基础鉴权 + 交易所集成 |
| M3-M4 | 核心交易能力 | Trading Engine + 风控链 + IoTDB |
| M5-M6 | 完整运维能力 | 通知 + 回测 + 仪表盘 + 审计 |
| M7 | 质量加固 | 全量测试 + 性能调优 + 边界调研 |
| **Post-MVP** | **高可用演进** | 如需要：Redis 分布式缓存 → 多实例 Trading Engine → 分布式锁 → 读写分离 |
| **Post-MVP** | **期货支持** | 多空双向、杠杆、保证金管理（需全新 RiskContext） |
