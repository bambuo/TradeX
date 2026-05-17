## 1. Goal
修复 `/backtests` 页面点击“开始回测”后，回测任务未出现在任务表格中的问题，并让失败原因可见、可回归验证。

## 2. Approach
当前问题有两个明确断点：后端实际路由是 `/api/backtests...`，但 Casbin 策略仍写成旧的 `/api/traders/*/strategies/*/backtests...`；前端列表刷新又通过“交易员 → 策略绑定 → strategyId”间接查询并吞掉错误，导致 403 或未绑定策略都会表现为空表。修复优先保持现有接口风格，补授权、补全列表查询能力，并把前端刷新逻辑收敛成一个可复用 `loadTasks()`，避免创建后和初始化使用两套隐式流程。

## 3. File Changes
- Modify `/Users/johana/Codes/github/TradeX/backend/TradeX.Infrastructure/Casbin/policy.csv`: 在 admin/operator/viewer 的授权段加入实际生效的 `/api/backtests`、`/api/backtests/tasks`、`/api/backtests/tasks/*`、`/api/backtests/tasks/*/result`、`/api/backtests/tasks/*/analysis`、`/api/backtests/tasks/*/analysis/stream` 权限；保留旧规则以免其它入口仍依赖旧路径。
- Modify `/Users/johana/Codes/github/TradeX/backend/TradeX.Core/Interfaces/IBacktestTaskRepository.cs`: 增加按当前列表需求查询任务的方法，例如 `GetAllAsync(CancellationToken)` 或后续若引入用户隔离则使用 `GetByCreatedByAsync(Guid, CancellationToken)`。
- Modify `/Users/johana/Codes/github/TradeX/backend/TradeX.Infrastructure/Data/Repositories/BacktestTaskRepository.cs`: 实现新增仓库方法，按 `CreatedAt` 倒序返回任务；现有 `GetByStrategyIdAsync` 第 13-17 行保留用于策略过滤。
- Modify `/Users/johana/Codes/github/TradeX/backend/TradeX.Trading/Backtest/IBacktestService.cs`: 暴露列表查询方法，例如 `GetTasksAsync(Guid? strategyId, CancellationToken)`，让 Controller 不直接依赖仓库细节。
- Modify `/Users/johana/Codes/github/TradeX/backend/TradeX.Trading/Backtest/BacktestService.cs`: 在现有 `StartBacktestAsync` 第 15-44 行不变的基础上，实现新列表方法；`strategyId` 有值时沿用 `GetByStrategyIdAsync`，无值时返回全局任务列表。
- Modify `/Users/johana/Codes/github/TradeX/backend/TradeX.Api/Controllers/BacktestingController.cs`: 调整 `GetTasks` 第 51-66 行，取消 `strategyId` 为空直接返回空数组的行为，改为返回页面任务列表；继续支持 `strategyId` 过滤。必要时在返回 DTO 中补齐 `ExchangeId`，便于前端展示和测试。
- Modify `/Users/johana/Codes/github/TradeX/frontend/src/api/backtests.ts`: 让 `getTasks` 的 `strategyId` 参数可选；无参数时请求 `/backtests/tasks`，有参数时保留当前 `/backtests/tasks?strategyId=...`。
- Modify `/Users/johana/Codes/github/TradeX/frontend/src/views/BacktestListView.vue`: 抽取 `loadTasks()`，`onMounted` 和 `save()` 共用；`save()` 第 253-286 行创建成功后优先将返回任务追加到表格或调用 `loadTasks()`；移除第 155、157、281、283 行的空 `catch {}`，至少设置 `error` 或 `formError`，避免授权失败被伪装成“暂无任务”。
- Modify `/Users/johana/Codes/github/TradeX/backend/TradeX.Tests/Trading/BacktestServiceTests.cs`: 增加服务层任务列表查询测试，覆盖 `strategyId` 过滤与无过滤列表。
- Create `/Users/johana/Codes/github/TradeX/backend/TradeX.Tests/Api/BacktestingControllerAuthorizationTests.cs`: 如项目已有 API 测试基建则新增；验证 admin/operator 可 POST `/api/backtests` 和 GET `/api/backtests/tasks`，viewer 只能 GET，不能 POST。

## 4. Implementation Steps
### Task 1: 修复授权断点
1. 在 `/Users/johana/Codes/github/TradeX/backend/TradeX.Infrastructure/Casbin/policy.csv` 的 admin 段第 20-23 行附近补充实际 `/api/backtests...` 规则，POST 仅给 admin/operator，GET 给 admin/operator/viewer。
2. 同步在 operator 段第 58-61 行附近、viewer 段第 84-86 行附近补充对应 GET/POST 权限。
3. 保留 `/api/traders/*/strategies/*/backtests...` 旧规则，降低对旧入口或历史测试的影响。

### Task 2: 让后端列表接口能服务 `/backtests` 全局页面
1. 在 `/Users/johana/Codes/github/TradeX/backend/TradeX.Core/Interfaces/IBacktestTaskRepository.cs` 增加 `GetAllAsync(CancellationToken ct = default)`。
2. 在 `/Users/johana/Codes/github/TradeX/backend/TradeX.Infrastructure/Data/Repositories/BacktestTaskRepository.cs` 用 `context.BacktestTasks.OrderByDescending(t => t.CreatedAt).ToListAsync(ct)` 实现。
3. 在 `/Users/johana/Codes/github/TradeX/backend/TradeX.Trading/Backtest/IBacktestService.cs` 增加 `GetTasksAsync(Guid? strategyId = null, CancellationToken ct = default)`。
4. 在 `/Users/johana/Codes/github/TradeX/backend/TradeX.Trading/Backtest/BacktestService.cs` 实现：`strategyId` 有效则调用现有 `GetByStrategyIdAsync`，否则调用新增 `GetAllAsync`。
5. 在 `/Users/johana/Codes/github/TradeX/backend/TradeX.Api/Controllers/BacktestingController.cs` 的 `GetTasks` 中删除空 `strategyId` 返回空数组逻辑，改为调用服务层新方法。

### Task 3: 前端统一刷新逻辑并暴露错误
1. 在 `/Users/johana/Codes/github/TradeX/frontend/src/api/backtests.ts` 将 `getTasks(strategyId: string)` 改为 `getTasks(strategyId?: string)`，按参数拼接 URL。
2. 在 `/Users/johana/Codes/github/TradeX/frontend/src/views/BacktestListView.vue` 抽出 `async function loadTasks()`，直接调用 `backtestsApi.getTasks()` 获取全局任务列表。
3. 在 `onMounted` 中先加载 traders/strategies/exchanges，再调用 `loadTasks()`；不再通过每个交易员绑定循环查询任务。
4. 在 `save()` 创建每个 pair 后，关闭弹窗并调用 `await loadTasks()`；如果列表 API 失败，将后端 `message` 或 `error` 显示到 `formError`/`error`。
5. 删除静默空 `catch {}`，至少记录页面错误状态，避免 403、500、网络失败看起来像空数据。

### Task 4: 补测试与回归
1. 在 `/Users/johana/Codes/github/TradeX/backend/TradeX.Tests/Trading/BacktestServiceTests.cs` 增加无 strategyId 返回任务列表的测试。
2. 若现有测试基建允许，创建 `/Users/johana/Codes/github/TradeX/backend/TradeX.Tests/Api/BacktestingControllerAuthorizationTests.cs` 验证 Casbin 策略对真实路径生效。
3. 手动回归 `/backtests` 页面：用 admin/operator 创建回测后，表格立刻出现 Pending/Running/Completed 任一状态的任务；viewer 能看列表但不能创建。

## 5. Acceptance Criteria
- admin 用户请求 `POST /api/backtests?...` 返回 200，并返回 `taskId`、`status`、`createdAt`。
- operator 用户请求 `POST /api/backtests?...` 返回 200，并返回 `taskId`。
- viewer 用户请求 `POST /api/backtests?...` 返回 403。
- admin/operator/viewer 请求 `GET /api/backtests/tasks` 返回 200；当数据库存在至少 1 条 `BacktestTasks` 时，响应数组长度至少为 1。
- `GET /api/backtests/tasks?strategyId={id}` 仍只返回该策略的任务。
- `/backtests` 页面创建一个回测任务后，无需刷新浏览器，表格中出现新任务行。
- 当 `GET /api/backtests/tasks` 返回 403/500 时，页面显示错误信息，而不是显示“暂无回测任务”。
- 后端 `dotnet test backend/TradeX.Tests/TradeX.Tests.csproj` 通过。
- 前端类型检查/构建命令通过；若项目脚本为 `npm run build`，则执行该命令并确认无 TypeScript 错误。

## 6. Verification Steps
- 运行 `dotnet test backend/TradeX.Tests/TradeX.Tests.csproj`。
- 运行前端构建命令，例如在 `/Users/johana/Codes/github/TradeX/frontend` 下执行 `npm run build`。
- 使用浏览器登录 admin 或 operator，进入 `/backtests`，点击“新建回测”，选择策略、交易所、交易对、时间范围和本金，点击“开始回测”，确认表格出现新任务。
- 使用浏览器开发者工具 Network 验证：创建请求为 `POST /api/backtests`，刷新列表请求为 `GET /api/backtests/tasks`，二者非 superadmin 下也不是 403。
- 使用 viewer 登录，确认能加载 `/backtests` 表格，但点击创建或直接 POST 返回 403。
- 选择一个未绑定到交易员的全局策略创建回测，确认任务仍显示在 `/backtests` 全局列表。

## 7. Risks & Mitigations
- 风险：`GET /api/backtests/tasks` 返回全局任务可能暴露所有用户任务。缓解：当前策略接口 `/api/strategies` 已是全局返回；若要做用户隔离，应先修复 `Strategy.CreatedBy` 在创建时未赋值的问题，再把任务列表改为 `CreatedBy == CurrentUserId`。
- 风险：旧的交易员绑定列表中同一个策略可能被多个交易员绑定，旧前端拼装方式会重复显示同一任务。缓解：新前端直接使用全局任务接口，避免重复拼装。
- 风险：仅补前端追加返回值会掩盖后端列表 API 仍不可用。缓解：创建成功后仍调用 `loadTasks()`，并在失败时显示错误，保证真实列表链路被验证。