# TradeX — AI 助手指南

## 项目概述

TradeX 是一个自动化加密货币交易系统，基于 ASP.NET Core + Vue 3 构建。支持多交易所接入、技术指标分析、策略引擎、风控管理、回测验证及实时通知。

---

## 技术栈（不可协商）

| 项 | 值 |
|---|---|
| 目标框架 | `net10.0` |
| 语言版本 | C# 14（`LangVersion=14.0`） |
| 可空引用类型 | 启用（`Nullable=enable`） |
| 隐式 using | 启用（`ImplicitUsings=enable`） |
| 测试框架 | xUnit + NSubstitute |
| 前端 | Vue 3 + TypeScript + Vite |
| 前端组件库 | `@arco-design/web-vue` |
| 后端 ORM | EF Core + PostgreSQL（Npgsql） |

---

## 项目结构

```
backend/
├── TradeX.Api/              # ASP.NET Core Web API + SignalR Hubs
├── TradeX.Core/             # 纯领域模型、枚举、接口（叶节点，零依赖）
├── TradeX.Exchange/         # IExchangeClient + 各交易所实现
├── TradeX.Indicators/       # Skender.Stock.Indicators 封装
├── TradeX.Trading/          # 策略引擎 + 风控 + 回测 + 订单 reconciliation
├── TradeX.Infrastructure/   # EF Core + PostgreSQL + Casbin + Encryption
├── TradeX.Notifications/    # Telegram / Discord / Email
├── TradeX.Worker/           # 策略/回测 Worker 进程
└── TradeX.Tests/
```

**依赖方向**：`Api → Trading → Exchange, Indicators, Infrastructure, Notifications`。`Core` 为叶节点，不依赖任何其他项目。

---

## 编码规范

### 强制语法规则（C# 14）

所有代码必须遵循以下规则，**禁止例外**：

| 规则 | 说明 | 禁止 |
|---|---|---|
| 主构造函数 | 消除所有简单 DI 字段注入 | `private readonly` DI 字段 |
| 集合表达式 | `[]` 初始化空集合 | `new List<T>()`、`new Dictionary<K,V>()`、`Array.Empty<T>()` |
| `field` 关键字 | 替代手动 `_field` 声明 | 手动 `_field` 后备字段 |

> **主构造函数例外**：`IOptions<T>.Value` 延迟访问、循环依赖场景。

### 通用约定

- I/O 方法必须传递 `CancellationToken`，方法名以 `Async` 后缀
- 使用结构化日志（`ILogger<T>`），禁止字符串拼接
- 禁止 Service Locator 模式（仅 `Program.cs` 允许）
- Casbin 策略需为新 API 端点添加对应规则

---

## 构建与运行

```bash
# 构建所有项目
dotnet build backend/TradeX.sln

# 运行测试
dotnet test backend/TradeX.Tests

# 启动 API 服务
dotnet run --project backend/TradeX.Api
```

---

## 沟通规范

- **回复语言**：AI 助理在回复时**只能使用中文**，禁止使用其他语言
- **图表渲染**：需要展示流程图、架构图、状态图、时序图等图表时，**一律使用 `.md` 文件所支持的渲染语法**（如 Mermaid 的 ` ```mermaid ` 代码块）。**禁止使用文字画图**（如 ASCII art 拼凑的框线箭头、表格拼凑的模拟图等）
- **Mermaid 换行**：Mermaid 节点文本中**不支持 `\n` 换行符**，如需换行必须使用 HTML 标签 `<br/>`（如 `"状态一<br/>状态二"`）
