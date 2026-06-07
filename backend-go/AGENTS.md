# TradeX — Go 后端 AI 助手指南

## 项目概述

TradeX 自动化加密货币交易系统 Go 后端。基于 DDD 四层架构，支持回测引擎、实盘交易、多交易所接入、技术指标分析、风控管理。

---

## DDD 与整洁架构（不可违反）

本项目严格执行 DDD 战术模式 + 整洁架构，**任何代码变更必须遵守以下分层铁律**：

### 分层依赖铁律

```
cmd/cli/        → 组装依赖，启动进程
internal/worker/, internal/server/  → 应用层，编排用例
internal/trading/                   → 交易服务层
internal/infra/                     → 基础设施层
internal/domain/                    → 领域层（叶节点，禁止任何导入）
```

| 层级 | 允许依赖 | 禁止 |
|---|---|---|
| 领域层 `domain/` | 标准库、`shopspring/decimal`、`github.com/google/uuid` | 任何 `infra/`、`server/`、`trading/`、`worker/`、Gin、Ent、Redis |
| 基础设施层 `infra/` | `domain/`、第三方框架（Gin/Ent/Redis/Binance…） | 反向依赖 `server/`、`trading/` |
| 交易层 `trading/` | `domain/`、`infra/` 接口 | 反向依赖 `server/`、`worker/` |
| 应用层 `server/`、`worker/` | `domain/`、`infra/`、`trading/` | — |

### DDD 战术守住原则

1. **实体/值对象** — 只存在于 `domain/`，封装业务行为（`Order.Fill()`、`Position.Close()`）
2. **仓储接口** — 定义在 `domain/`，实现注入 `infra/persistence/`
3. **领域事件** — 定义在 `domain/`，发布在 `infra/eventbus/` 或 `trading/eventbus.go`
4. **应用服务** — 编排用例流程，不包含业务逻辑（委派给领域实体+仓储）
5. **依赖注入** — 构造函数注入，`NewXxx(deps…)` 模式，禁止 Service Locator、全局变量、`init()` 初始化业务依赖

### 违规示例（一票否决）

```go
// ❌ 领域层导入了 Ent
import "tradex/internal/infra/ent"

// ❌ 仓储直接在领域层实现
type OrderRepository struct { db *ent.Client }

// ❌ Service Locator
var GlobalRepo domain.OrderRepository

// ❌ 应用服务中嵌入 SQL
func (s *Svc) DoSomething() { s.db.QueryRow("SELECT ...") }

// ❌ 通过 init() 注册业务依赖
func init() { svc = NewService(db) }
```

---

## 技术栈（不可协商）

| 项 | 值 |
|---|---|
| 语言 | Go 1.26.1 |
| 模块名 | `tradex` |
| Web 框架 | Gin |
| ORM | Ent（`entgo.io/ent`） |
| 数据库 | PostgreSQL 16+（pgx v5） |
| 缓存/消息 | Redis 7+（go-redis） |
| 日志 | zerolog |
| 配置 | Viper + godotenv |
| CLI | Cobra |
| 测试 | testify + 标准库 `testing` |
| 遥测 | OpenTelemetry（OTLP HTTP） |
| 高精度 | shopspring/decimal |
| 交易所 | Binance（REST + WebSocket） |

---

## 项目结构

```
cmd/
└── server/main.go          # 唯一入口，调用 cli.Execute()

internal/
├── cli/                    # Cobra 命令：api / backtest / worker
├── config/                 # 环境变量加载
├── domain/                 # 领域层（叶节点，零依赖）
│   ├── backtest/           # 回测子领域 + engine/ 回测引擎
│   ├── indicator/          # 技术指标（SMA/EMA/RSI/MACD/Bollinger/Stochastic）
│   ├── candle.go, order.go, position.go, strategy.go, enums.go, errors.go ...
│   └── repositories.go, strategy_repository.go  # 仓储接口
├── infra/                  # 基础设施层
│   ├── analysis/           # 回测分析内存存储
│   ├── crypto/             # AES-GCM 加解密
│   ├── ent/                # Ent 代码生成 + Schema
│   ├── eventbus/           # EventBus（Null / Redis Pub/Sub + Stream）
│   ├── exchange/           # Binance 适配器（REST + WebSocket）
│   ├── persistence/        # 仓储实现（Ent）
│   ├── telemetry/          # OTel 初始化
│   └── worker/             # 回测 Worker 基础设施（调度器/队列/监听/资源监控）
├── server/                 # 接口层 + 应用层
│   ├── api/handler/        # Gin handler + 中间件 + 统一响应
│   └── app/backtest/       # 回测应用服务
├── trading/                # 实盘交易层
│   ├── streaming/          # K 线/成交 WebSocket 订阅管理
│   ├── fill_projector.go, order_reconciler.go, position_reconciler.go
│   ├── risk.go             # 组合级风控（9 项检查链）
│   ├── grid.go             # 波动率网格算法
│   ├── eventbus.go         # 领域事件总线（Redis Stream）
│   └── killswitch.go, client_provider.go, metrics.go, settings.go
└── worker/                 # 实盘 Worker 编排
    ├── app.go              # errgroup 服务容器 + 优雅停机
    ├── guard.go            # PG advisory lock 单实例守卫
    ├── exchange_order_sync.go
    └── order_reconciler_service.go

docs/                       # 项目文档
    ├── Go代码规范.md        # 代码规范 v2.0
    └── 技术选型清单.md      # 技术选型 v1.0
```

**依赖方向**：`domain(叶节点) ← infra/server/trading ← worker/cli`。

**三个入口**（通过子命令路由）：

| 命令 | 进程 | 职责 |
|---|---|---|
| `tradex api` | API 服务器 | Gin HTTP（REST + SSE） |
| `tradex backtest` | 回测 Worker | 消费队列，调度回测引擎 |
| `tradex worker` | 实盘 Worker | 定序对账/持仓对账/历史订单同步 |

---

## 编码规范

### 强制规则

| 规则 | 说明 | 禁止 |
|---|---|---|
| decimal 运算 | `shopspring/decimal` 处理金额 | `float64` 直接计算 |
| 接口隔离 | 仓储/服务接口定义在 `domain`，实现注入 | 全局变量 / Service Locator |
| 领域层零依赖 | `domain/` 不导入任何 `infra/`、`server/`、`trading/` 包 | 倒置依赖 |
| 结构化日志 | `zerolog` 链式调用，日志字段键值化 | 字符串拼接、`fmt.Println` |
| 主键 | `uuid.New()` 生成实体 ID | 自增 int 主键 |
| 数字精度 | decimal 比较使用 `Cmp()` | `==` 直接比较 decimal |

### 命名规范

| 元素 | 规范 | 示例 |
|---|---|---|
| 包名 | 全小写单数 | `package domain`、`package trading` |
| 文件 | 小写+下划线 | `order_reconciler.go`、`kline_stream_manager.go` |
| 接口 | 导出 + `er` 后缀 | `OrderRepository`、`DomainEventBus`、`Pinger` |
| 错误变量 | `Err` 开头 | `ErrNotFound`、`ErrInvalidInput` |
| 测试函数 | `TestXxx_Yyy` | `TestPositionReconciler_CriticalDriftWhenLocalExceedsActual` |
| 接收者 | 单字母/简短 | `r *OrderReconciler`、`m *PortfolioRiskManager` |

### 通用约定

- context 作为函数第一个参数，错误作为最后一个返回值
- 错误包装使用 `fmt.Errorf` + `%w`
- 导入分组：标准库 → 第三方 → 本地包（空行分隔）
- 使用 `errgroup` 管理并发 goroutine
- `t.Skip()` 跳过需要外部数据库的集成测试（检查 `DATABASE_DSN` 环境变量）

---

## 构建与运行

```bash
# 构建所有目标
go build -o bin/tradex ./cmd/server

# 运行测试（含竞态检测）
go test ./... -v -count=1 -race

# 运行指定测试（回测引擎）
go test ./internal/domain/backtest/engine/ -run Parity

# 代码检查
golangci-lint run ./...

# 启动 API 服务（依赖 docker-compose 中的 postgres + redis）
go run ./cmd/server api

# 启动回测 Worker
go run ./cmd/server backtest

# 启动实盘 Worker
go run ./cmd/server worker

# 全量环境启动
make build && docker compose up -d
```

---

## 沟通规范

- **回复语言**：AI 助理在回复时只能使用中文
- **图表渲染**：需要展示流程图、架构图、状态图、时序图时，一律使用 `.md` 文件所支持的 Mermaid 语法，禁止文字画图
- **Mermaid 换行**：节点文本中不支持 `\n` 换行符，如需换行必须使用 HTML 标签 `<br/>`
