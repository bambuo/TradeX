# AGENTS.md — TradeX 编码规范

AI 代理唯一编码准则。不遵守 = 拒收 PR。

---

## 1. 技术栈（不可协商）

| 项 | 值 |
|---|---|
| TargetFramework | `net10.0` |
| LangVersion | `preview` |
| Nullable | `enable` |
| ImplicitUsings | `enable` |
| 测试 | xUnit + NSubstitute |
| 前端 | Vue 3 + TypeScript + Pinia |

所有代码必须使用以下 C# 14 语法：
- **主构造函数** — 消除所有简单 DI 字段注入。例外：`IOptions<T>.Value` 延迟访问、循环依赖
- **集合表达式** — `[]` 替代 `new List<T>()` / `Array.Empty<T>()` / `new Dictionary<K,V>()`
- **`field` 关键字** — 代替手动 `_field` 声明
- 禁止：`new List<T>()`、`new Dictionary<K,V>()`、`private readonly` DI 字段、`net8.0`/`net9.0`

---

## 2. 项目结构 & 依赖方向

```
backend/
├── TradeX.Api/            # ASP.NET Core + SignalR Hubs
├── TradeX.Core/           # 纯领域模型、枚举、接口（叶节点，零依赖）
├── TradeX.Exchange/       # IExchangeClient + 各交易所实现
├── TradeX.Indicators/     # Trady 封装
├── TradeX.Trading/        # 策略引擎 + 风控 + 回测 + 订单 reconciliation
├── TradeX.Infrastructure/ # EF Core + SQLite + Casbin + IoTDB
├── TradeX.Notifications/  # Telegram / Discord / Email
└── TradeX.Tests/
```

依赖方向：`Api → Trading → Exchange, Indicators, Infrastructure, Notifications`。**Core 不依赖任何项目**。

---

## 3. 设计模式

| 模式 | 场景 |
|---|---|
| Strategy + DI | 可替换算法（费用计算、滑点、条件评估） |
| Options | 强类型配置绑定，`IOptions<T>` 注入 |
| Singleton | 无状态/共享服务（ExchangeClientManager, CasbinEnforcer, IRateLimiter） |
| Factory | 运行时条件创建对象（不同交易所实例） |
| Repository | 数据持久化抽象 |
| Chain of Responsibility | 风控管线：日亏损 → 回撤 → 连续亏损 → 熔断 → 滑点 |

**禁止**：Service Locator（`GetService<T>()`，仅 `Program.cs` 允许）、God Object（类 ≤300 行，方法 ≤50 行）、Magic String/Number、`catch(Exception)` 吞异常。

---

## 4. DI 规范

- **Singleton**：无状态/共享服务
- **Scoped**：ASP.NET Core Controller（默认）
- **Transient**：轻量无状态（`IConditionEvaluator`、通知发送器）
- 禁止注入 `IServiceProvider`
- `IOptions<T>` 仅注入需要运行时读配置的服务

---

## 5. 命名 & 编码

| 元素 | 规则 |
|---|---|
| 类/方法 | PascalCase |
| 接口 | 前缀 `I` |
| 局部变量 | camelCase |
| 私有字段 | `_camelCase` |
| 参数 | camelCase |
| 异步方法 | 后缀 `Async` |
| 常量 | 统一风格：全 PascalCase 或 SCREAMING_SNAKE |

**异步**：I/O 用 `async Task` / `async Task<T>`，传 `CancellationToken`，禁止 `Wait()` / `.Result`。

**错误**：边界层 catch + log 返回默认值；内部层传播异常。结构化日志，禁止字符串拼接：
```csharp
_logger.LogWarning("余额不足: {Balance} < {Required}", balance, required); // ✅
_logger.LogWarning($"余额不足: {balance} < {required}");                   // ❌
```

**防御**：方法参数用 `IReadOnlyList<T>` 而非 `List<T>`。DTO 用 `record`。

---

## 6. 测试

- 每个接口对应测试类，命名 `[ClassName]Tests`
- 覆盖：正常路径 + 边界 + 异常
- 方法命名：`{MethodName}_{Scenario}_{ExpectedOutcome}`

```
TradeX.Tests/
├── Trading/   (TradeExecutor, ConditionEvaluator, RiskManager, OrderReconciler)
├── Infrastructure/ (CasbinAuthorization)
├── Core/      (Position)
└── Api/       (AuthController)
```

---

## 7. 构建

```bash
dotnet build && dotnet test && docker compose build
```

Docker 镜像发布，无单文件产物。

---

## 8. 审查清单

- [ ] `net10.0` + `LangVersion=preview` + Nullable 启用
- [ ] 主构造函数替代简单 DI 注入
- [ ] 集合用 `[]` 语法
- [ ] 无 magic string/number
- [ ] I/O 方法传递 `CancellationToken`，后缀 `Async`
- [ ] 异常边界层正确处理
- [ ] 新配置在对应 `Settings` 类有强类型属性
- [ ] 无 Service Locator
- [ ] Casbin 策略已为新 API 端点添加规则
- [ ] 前端新页面已添加路由 + 角色守卫
