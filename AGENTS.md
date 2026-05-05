# AGENTS.md — TradeX 编码规范

AI 代理唯一编码准则。不遵守 = 拒收 PR。

## AI 团队协作规范

> 本项目依赖一份完整的多角色分工协作规范来驱动 AI 工作。

### 首次指令（必须执行）

**你的第一条指令必须是**：读取并理解团队协作规范文件。

```
📖 加载团队协作规范
文件路径: ROLE.md
```

**禁止跳过此步骤。** 未加载该文件前，不得输出任何代码或执行任何操作。

### 工作流程

加载完成后：

1. 以 **PM（项目经理）** 角色按 `ROLE.md` §14 输出「PM 启动声明」
2. 按 §2「角色切换触发规则」匹配任务类型，确定执行链路
3. 每切换一个角色，按 §9 输出「角色激活声明」
4. 产出交付前按 §10 输出「闭环自检」，全部 ✅ 方可提交
5. 角色交接时按 §11 输出「角色交接协议」
6. 违反角色边界按 §12 进行熔断与回退

### 文件结构

| 文件 | 作用 |
|------|------|
| `AGENTS.md`（本文件） | **通用入口**，大多数 AI 工具自动识别。定义技术栈、项目结构、通讯规范 |
| `.trae/rules/project_rules.md` | Trae IDE 的自动加载入口，指向此处 |
| `ROLE.md` | **核心规范文件**，所有角色定义、闭环、防御机制、编码约定均在其中 |

---

## 技术栈（不可协商）

| 项 | 值 |
|---|---|
| TargetFramework | `net10.0` |
| LangVersion | `14.0` |
| Nullable | `enable` |
| ImplicitUsings | `enable` |
| 测试 | xUnit + NSubstitute |
| 前端 | Blazor Server (InteractiveServer) + Bootstrap Blazor |
| 前端 NuGet | `BootstrapBlazor` v10.6.0 |

所有代码必须使用以下 C# 14 语法：
- **主构造函数** — 消除所有简单 DI 字段注入。例外：`IOptions<T>.Value` 延迟访问、循环依赖
- **集合表达式** — `[]` 替代 `new List<T>()` / `Array.Empty<T>()` / `new Dictionary<K,V>()`
- **`field` 关键字** — 代替手动 `_field` 声明
- 禁止：`new List<T>()`、`new Dictionary<K,V>()`、`private readonly` DI 字段、`net8.0`/`net9.0`

---

## 项目结构 & 依赖方向

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

## 审查清单

- [ ] `net10.0` + `LangVersion=14.0` + Nullable 启用
- [ ] 主构造函数替代简单 DI 注入
- [ ] 集合用 `[]` 语法
- [ ] 无 Service Locator（仅 `Program.cs` 允许）
- [ ] I/O 方法传递 `CancellationToken`，后缀 `Async`
- [ ] 结构化日志，禁止字符串拼接
- [ ] Casbin 策略已为新 API 端点添加规则
- [ ] Blazor 新页面已添加路由 + 角色守卫
- [ ] 详细编码约定见 `ROLE.md`

---

## 沟通规范

- **回复语言**：AI 助理在回复时**只能使用中文**，禁止使用其他语言
- **图表渲染**：需要展示流程图、架构图、状态图、时序图等图表时，**一律使用 `.md` 文件所支持的渲染语法**（如 Mermaid 的 ` ```mermaid ` 代码块）。**禁止使用文字画图**（如 ASCII art 拼凑的框线箭头、表格拼凑的模拟图等）
- **Mermaid 换行**：Mermaid 节点文本中**不支持 `\n` 换行符**，如需换行必须使用 HTML 标签 `<br/>`（如 `"状态一<br/>状态二"`）
