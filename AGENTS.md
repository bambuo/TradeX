# AGENTS.md — BSC Smart Money Trading Bot 编码规范

本文件定义参与此项目的 AI 代理（Agent）和开发者的编码标准、架构约定和质量要求。

---

## 1. 技术栈硬性要求

### 1.1 框架与语言版本（不可协商）

| 项目 | 要求 | 说明 |
|------|------|------|
| **.NET SDK** | `net10.0` 及以上 | 项目 TargetFramework 必须为 `net10.0`，禁止使用 `net8.0`、`net9.0` |
| **C# 语言版本** | `preview`（C# 14） | `LangVersion` 必须设为 `preview` 以启用 C# 14 语法；`<LangVersion>preview</LangVersion>` |
| **Nullable** | 启用 | `<Nullable>enable</Nullable>` |
| **ImplicitUsings** | 启用 | `<ImplicitUsings>enable</ImplicitUsings>` |

### 1.2 必须使用的 C# 14 语法特性

所有新代码须优先使用以下 C# 14 及 .NET 10 语法：

- **主构造函数** — `public class Service(ILogger logger)`，代替在构造方法中手动赋值字段
- **集合表达式** — `[]` 代替 `new()`、`new List<T>()`、`Array.Empty<T>()`
- **`field` 关键字** — `field` 在属性 getter/setter 中访问后备字段，代替手动声明 `_field`
- **`params` 集合改进** — `params ReadOnlySpan<T>` 和 `params IEnumerable<T>`
- **Lambda 自然类型改进** — 充分利用编译器自动推断 Lambda 类型
- **内联数组** `[InlineArray]` — 用于高性能场景的固定大小缓冲区
- **`ref struct` 实现 `IDisposable`** — 允许 `ref struct` 实现 `IDisposable` 和 `IAsyncDisposable`
- **半自动属性** — `property { get; set => field = value; }` 避免冗余字段声明
- **`nameof` 作用域扩展** — `nameof` 可用于实例成员的未限定引用
- **一次构造 `List<T>` 和 `Dictionary<K,V>` 时指定初始容量** — 使用 `List<T>(capacity)` 和 `Dictionary<K,V>(capacity)` 构造函数

> 凡是能通过主构造函数消除的字段注入，必须使用主构造函数。例外：需要 IOptions\<T\>.Value 延迟访问、循环依赖或字段需要复杂初始化。

### 1.3 禁止使用的旧语法

- ❌ 显式 `new List<T>()` → 改用 `[]`
- ❌ 显式 `new Dictionary<K, V>()` → 改用 `[]` 或 `new(capacity)`
- ❌ `private readonly` 字段用于 DI 注入 → 改用主构造函数参数
- ❌ `.NET 8` / `.NET 9` target framework → 必须使用 `net10.0`

---

## 2. 架构原则

### 2.1 分层架构

项目采用经典三层架构：

```
BscSmartMoneyBot/
├── Core/                    # 领域层：纯POCO模型、枚举
│   ├── Models/
│   └── Enums/
├── Configuration/           # 配置层：强类型 Options 模式
├── Commands/                # 命令行解析层
├── Services/
│   ├── Interfaces/          # 接口定义（ISP 原则）
│   └── Implementations/     # 实现层
├── HostedServices/          # 后台服务（IHostedService）
└── Program.cs               # 入口 + DI 注册
```

**依赖方向：** `HostedServices → Services.Interfaces → Services.Implementations`。`Core` 和 `Configuration` 是叶节点，不依赖任何 Service。

### 2.2 必须使用的设计模式

| 模式 | 使用场景 | 示例 |
|------|----------|------|
| **Strategy** | 可替换的算法实现 | 不同的费用计算策略、滑点计算策略、信号评分策略 |
| **Strategy + DI** | 运行时注入策略实现 | 通过 DI 容器注入 `IFeeCalculator` 的不同实现 |
| **Options 模式** | 强类型配置绑定 | `IOptions<BotSettings>` 注入配置节 |
| **Builder 模式** | 对象构建（非可选） | DI 容器使用 `Host.CreateDefaultBuilder()` 是标准用法 |
| **Singleton** | 无状态/共享状态服务 | `OnchainOSClient`、`StateManager` |
| **Factory 模式** | 复杂对象创建 | 当需要根据运行时条件创建不同策略实例时 |
| **Repository 模式** | 数据持久化抽象 | `IStateManager` 抽象状态读写 |
| **Observer / 事件驱动** | 状态变更通知 | 推荐：`IStateChangedHandler` 替代直接调用 `SaveStateAsync` |
| **Chain of Responsibility** | 处理管道 | 信号处理管线：Fetch → Filter → Scan → Execute |

### 2.3 不允许的反模式

- ❌ **Service Locator** — 禁止从容器手动解析服务（`serviceProvider.GetService<T>()`）
- ❌ **God Object** — 任何类不应超过 300 行；单方法不应超过 50 行
- ❌ **Stringly-Typed 配置** — 配置值必须绑定到强类型 `Settings` 类，禁止在代码中硬编码配置键字符串
- ❌ **Magic Number / String** — 所有常量必须声明为 `private const` 或 `static readonly`
- ❌ **catch(Exception) 吞异常** — 允许日志记录，但必须 `throw` 或返回到已知错误状态，不可静默消化

---

## 3. 代码质量规范

### 3.1 命名约定

| 元素 | 规则 | 示例 |
|------|------|------|
| 类 | PascalCase | `TradeExecutor` |
| 接口 | 前缀 `I` | `ITradeExecutor` |
| 方法 | PascalCase | `ExecuteBuyAsync` |
| 局部变量 | camelCase | `buyAmount` |
| 私有字段 | `_camelCase` | `_stateManager` |
| 常量 | PascalCase 或 `SCREAMING_SNAKE_CASE`（选其一，保持统一） | `DefaultSlippage` / `DEFAULT_SLIPPAGE` |
| 异步方法 | 后缀 `Async` | `LoadStateAsync` |
| 参数 | camelCase | `CancellationToken ct` |

### 3.2 异步规范

- 所有 I/O 方法必须标记为 `async Task` / `async Task<T>`（用 `ValueTask<T>` 仅当是高频热路径）
- 使用 `CancellationToken` 传播，从 `BackgroundService.ExecuteAsync` 直到最内层的 I/O 调用
- `Task.WhenAll` 用于并行调用，`Task.WhenAny` 用于超时/取消
- 避免 `Wait()` 和 `Result` — 不使用阻塞等待

### 3.3 错误处理

- **边界层吞异常 + 日志**：`Services.Implementations` 中的公共方法可以在最外层 catch 并记录日志，返回默认值
- **内部方法传播异常**：`OnchainOSClient.ExecuteCommandAsync` 等内部辅助方法应当传播异常
- **使用结构化日志**：始终使用 `_logger.LogInformation/LogWarning/LogError` 配合模板占位符，禁止字符串拼接

```csharp
// ✅ 正确
_logger.LogWarning("余额不足: {Balance} < {Required}", balance, required);

// ❌ 错误
_logger.LogWarning($"余额不足: {balance} < {required}");
```

### 3.4 不可变性与防御

- 接口方法参数使用 `IReadOnlyList<T>` / `IReadOnlyDictionary<K,V>` 而非 `List<T>` / `Dictionary<K,V>`，除非方法需要修改集合
- 核心领域模型（`BotState`、`Position`）的集合属性应优先用 `ImmutableDictionary` 或返回防御性副本
- `record` 类型适用于数据传输（DTO/result 类型）

---

## 4. 测试要求

### 4.1 自动化测试

- **所有接口**（`Interfaces/`）必须有对应的测试类
- 测试框架：**xUnit**（首选）
- Mock 框架：**NSubstitute** 或 **Moq**
- 测试覆盖：
  - 正常路径（happy path）
  - 边界条件（空列表、0 值、负数、null）
  - 异常路径（API 超时、网络错误、数据损坏）
- 以 `*Tests` 结尾命名测试项目，如 `BscSmartMoneyBot.Tests`
- 测试类以 `[ServiceName]Tests` 格式命名

### 4.2 命名与结构

```
test/BscSmartMoneyBot.Tests/
├── Services/
│   ├── TradeExecutorTests.cs
│   ├── SignalMonitorTests.cs
│   └── StateManagerTests.cs
├── Core/
│   └── PositionTests.cs          # 领域逻辑测试
└── Configuration/
    └── BotSettingsTests.cs
```

测试方法命名：`{MethodName}_{Scenario}_{ExpectedOutcome}`

```csharp
[Fact]
public async Task ExecuteBuyAsync_DryRunMode_CreatesPositionInState()
{
    // Arrange
    // Act
    // Assert
}
```

---

## 5. DI 与依赖注入规范

### 5.1 注册规则

- **Singleton**：无状态服务、共享状态服务（`OnchainOSClient`、`StateManager`）
- **Scoped**：按请求隔离的服务（当前项目不需要）
- **Transient**：轻量级无状态服务（`TradeExecutor` — 如果有多个实例需求时）

当前项目默认使用 Singleton，除非明确需要不同生命周期。

### 5.2 注入规则

- 所有注入必须通过构造函数（或主构造函数）完成
- 禁止 `IServiceProvider` 注入（Service Locator）
- `IOptions<T>` 只注入到需要运行时读取配置的服务；静态配置在注册时直接绑定

---

## 6. 构建与发布

### 6.1 构建脚本

```bash
# 调试构建
dotnet build

# 单文件发布
dotnet publish -c Release -r osx-arm64

# 验证标准合规性
bash verify_standards.sh
```

### 6.2 发布产物

```
publish/
├── BscSmartMoneyBot       # 单文件可执行文件
├── appsettings.json
└── appsettings.*.json     # 环境配置
```

---

## 7. 代码审查检查清单

在提交 PR 或完成一个功能前，逐项检查：

- [ ] 使用 `net10.0` 且 `LangVersion=preview`
- [ ] Nullable 启用，无新增 null 警告
- [ ] 主构造函数替代了所有简单 DI 注入
- [ ] 集合使用了 `[]` 语法
- [ ] 无 magic string/number
- [ ] 所有 I/O 方法传递了 `CancellationToken`
- [ ] 异步方法后缀 `Async`
- [ ] 异常在最外层正确处理（日志 + 返回已知默认值 or 传播）
- [ ] 新配置文件在 `BotSettings.cs` 中有对应强类型类
- [ ] 无 Service Locator（`GetService<T>()`、`GetRequiredService<T>()` 仅在 `Program.cs` 中允许）

---

## 8. 此文件的维护

此文件由项目负责人维护。Agent 在引入新约定或修改现有规范时必须同时更新此文件。任何技术栈变更（升级 .NET 版本、引入新框架/模式）必须在 PR 描述中引用此文件并说明变更理由。
