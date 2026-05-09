# TradeX — 测试用例文档 (Test Cases)

## 文档信息

| 项目 | 内容 |
|------|------|
| 文档版本 | v1.3 |
| 文档状态 | Draft |
| 基于 PRD | `docs/PRD.md` v2.6 |
| 基于 FSD | `docs/FSD.md` v1.10 |
| 基于 TAD | `docs/TAD.md` v1.3 |
| 更新时间 | 2026-04-27 |
| 适用阶段 | M1-M7 测试与验收 |
| 测试框架 | 后端: xUnit + NSubstitute，前端: bUnit |

---

## 测试策略总览

### 测试分层

| 层级 | 覆盖范围 | 工具 | 目标覆盖率 |
|------|---------|------|-----------|
| 单元测试 | Core 领域模型、Indicators、条件树评估、风控各 Handler | xUnit + NSubstitute | ≥ 90% |
| 集成测试 | API Controller、Repository、Exchange Client、WebSocket Manager | xUnit + TestContainers / Mock | ≥ 80% |
| 端到端测试 | 完整用户流程（初始化→登录→建策略→回测→交易）| Playwright | 关键路径 100% |
| 性能测试 | Trading Engine 评估周期、API 响应时间、WebSocket 延迟 | k6 / BenchmarkDotNet | 满足 SLA |
| 安全测试 | MFA、Casbin RBAC、AES 加密、IP 白名单、SQL 注入 | 手动 + 自动化 | 全部安全场景 |
| 调研测试 | 各交易所限价单规则、WS 断线补发机制 | 手动验证 | N/A |

### 测试环境

| 环境 | 用途 | 配置 |
|------|------|------|
| T1 — 单元测试 | CI 流水线每次提交触发 | `dotnet test`，内存 SQLite |
| T2 — 集成测试 | 每日构建 | 真实 SQLite 文件 + Mock 交易所 |
| T3 — 预发布 (Staging) | 上线前回归 | Docker Compose 全栈部署 + 沙箱交易所 |
| T4 — 生产 | 监控与验证 | 生产环境 + 只读 API Key |

### 验收标准 (AC) 与测试用例映射

| PRD 验收标准 | 覆盖测试用例 |
|-------------|-------------|
| AC-01: docker-compose up 可访问 | TC-DEPLOY-001 ~ TC-DEPLOY-006 |
| AC-02: 首次访问→初始化向导→恢复正常 | TC-SETUP-001 ~ TC-SETUP-007 |
| AC-03: Super Admin 登录+MFA | TC-AUTH-001 ~ TC-AUTH-015 |
| AC-04: 创建用户→MFA 绑定→登录 | TC-AUTH-016 ~ TC-AUTH-025 |
| AC-05: Viewer 访问 POST API 返回 403 | TC-AUTH-026 ~ TC-AUTH-035 |
| AC-06: 添加交易所→测试连接→展示规则+资产 | TC-EXCH-001 ~ TC-EXCH-021, TC-EXCH-SYM-001 ~ TC-EXCH-SYM-004 |
| AC-07: 创建策略模板→部署→回测→启用 | TC-STRAT-001 ~ TC-STRAT-030, TC-STRAT-DEPLOY-001 ~ TC-STRAT-DEPLOY-010 |
| AC-08: 同 Pair 活跃策略冲突拒绝 | TC-STRAT-DEPLOY-007 |
| AC-09: 滑点控制正常/拒绝 | TC-TRADE-001 ~ TC-TRADE-015 |
| AC-10: 风控按配置触发 | TC-RISK-001 ~ TC-RISK-050 |
| AC-10-B: Kill Switch 生效 | TC-RISK-035 ~ TC-RISK-040 |
| AC-10-C: IP 白名单拦截/放行 | TC-SEC-010 ~ TC-SEC-015 |
| AC-11: 崩溃恢复+同步 | TC-RECOV-001 ~ TC-RECOV-008, TC-RECONCILER |
| AC-12: SignalR 实时推送 | TC-DATA-017 ~ TC-DATA-022 |
| AC-13: 通知推送 | TC-NOTIF-001 ~ TC-NOTIF-012 |
| AC-14: 审计日志记录与查询 | TC-AUDIT-001 ~ TC-AUDIT-010 |
| AC-15: 订单归档与导出 | TC-DATA-013 ~ TC-DATA-016 |
| AC-16: 回测运行+绩效报告+K 线回放+并发调度 | TC-BACKTEST-001 ~ TC-BACKTEST-032 |
| AC-17: Swagger 环境隔离 | TC-DEPLOY-010 |
| AC-18: dotnet test 全量通过 | TC-BUILD-001 |
| AC-19: Volatility Grid 风格策略行为一致 | TC-STRAT-VG-001 ~ TC-STRAT-VG-010 |

---

## 测试用例编号规则

```
TC-{模块}-{序号}
```

| 前缀 | 模块 |
|------|------|
| SETUP | 系统初始化 |
| AUTH | 用户与鉴权 (含 MFA) |
| CASBIN | Casbin 权限 |
| EXCH | 交易所管理/抽象层 |
| EXCH-SYM | 交易所交易对/资产查询 |
| STRAT | 策略模板 |
| STRAT-DEPLOY | 策略部署 (TradersStrategiesController) |
| STRAT-VG | Volatility Grid 策略专项 |
| INDIC | 技术指标 |
| DATA | 实时数据管道/数据管理 |
| TRADE | 交易执行 |
| RISK | 风控 (含多层级风控) |
| BACKTEST | 回测引擎 |
| BACKTEST-SCHED | 回测并发调度 |
| NOTIF | 通知 |
| AUDIT | 审计日志 |
| DASH | 仪表盘 |
| RECOV | 崩溃恢复/Reconciliation |
| DEPLOY | Docker 部署 |
| SEC | 安全 |
| PERF | 性能 |
| BUILD | 构建与测试 |
| WS | WebSocket 连接管理 |
| RATE | 限流层 |
| TRAIL | 移动止损/止盈 |
| SETTINGS | 系统设置 |
| ERR | 错误处理与中间件 |
| RECONCILER | 订单同步 Reconciliation |
| RESOURCE | 系统资源监控 |
| TRADER | 交易员管理 |

---

## M1 — 系统初始化 (Setup)

## M4 — Volatility Grid 策略专项

### TC-STRAT-VG-001: 波幅触发首单
- **前置条件**: 策略为 `volatility_grid`，`entryVolatilityPercent=1.0`，无持仓
- **测试步骤**:
  1. 注入一根 `RANGE_PCT=1.2` 的 K 线
  2. 执行一次策略评估
- **预期结果**: 触发首单买入，生成 1 笔开仓订单

### TC-STRAT-VG-002: 波幅不足不触发首单
- **前置条件**: 同上
- **测试步骤**:
  1. 注入一根 `RANGE_PCT=0.8` 的 K 线
  2. 执行一次策略评估
- **预期结果**: 不下单

### TC-STRAT-VG-003: 价格低于均价 1% 触发加仓
- **前置条件**: 已有持仓，均价 `100`
- **测试步骤**:
  1. 输入当前价 `99`（或更低）
  2. 执行评估
- **预期结果**: 触发加仓，`pyramidingCount + 1`

### TC-STRAT-VG-004: 价格高于均价 1% 触发减仓
- **前置条件**: 已有持仓，均价 `100`
- **测试步骤**:
  1. 输入当前价 `101`（或更高）
  2. 执行评估
- **预期结果**: 触发减仓

### TC-STRAT-VG-005: 达到最大追加次数后禁止继续加仓
- **前置条件**: `maxPyramidingLevels=5`，当前 `pyramidingCount=5`
- **测试步骤**:
  1. 输入满足加仓条件的价格
  2. 执行评估
- **预期结果**: 不触发加仓，下单数不增加

### TC-STRAT-VG-006: noStopLoss=true 不触发仓位止损
- **前置条件**: `noStopLoss=true`，价格快速下跌
- **测试步骤**:
  1. 执行评估循环
- **预期结果**: 仓位止损分支不执行

### TC-STRAT-VG-007: noStopLoss=true 仍受账户级风控限制
- **前置条件**: `noStopLoss=true`，触发日亏阈值
- **测试步骤**:
  1. 模拟累计亏损超过 `maxDailyLoss`
  2. 继续触发买入信号
- **预期结果**: 被风控拒绝，触发风险告警

### TC-STRAT-VG-008: 双周期部署去重
- **前置条件**: 同一策略部署 `5m` 与 `15m`
- **测试步骤**:
  1. 在去重窗口内让两条部署同时触发
- **预期结果**: 仅一笔订单成功执行

### TC-STRAT-VG-009: 回测与实盘决策一致
- **前置条件**: 同一组历史 K 线数据
- **测试步骤**:
  1. 执行回测
  2. 执行实盘模拟（mock 交易执行器）
- **预期结果**: 触发点、加减仓次数、最终仓位方向一致

### TC-STRAT-VG-010: 参数校验
- **前置条件**: 构造非法参数（如 `rebalancePercent <= 0`）
- **测试步骤**:
  1. 提交策略配置
- **预期结果**: 返回参数校验错误，策略不可保存/不可启用

---

## 交易员管理 (Trader)

### TC-TRADER-001: 禁用 Trader — 自动停用关联的 Active 策略
- **前置条件**: Trader 有 2 个 Active 策略
- **测试步骤**:
  1. `PUT /api/traders/:id/status` body: `{ "status": "Disabled" }`
- **预期结果**: 返回 200，Trader 状态为 Disabled，旗下 2 个策略状态自动变为 Disabled，日志记录策略停用操作
- **关联需求**: FSD §7.15, FSD §21.8

### TC-TRADER-002: 启用 Trader — 策略不自动恢复
- **前置条件**: Trader 为 Disabled，之前有 2 个策略被自动停用
- **测试步骤**:
  1. `PUT /api/traders/:id/status` body: `{ "status": "Active" }`
  2. 查询关联策略状态
- **预期结果**: Trader 状态为 Active，旗下策略保持 Disabled（需用户手动逐个启用）
- **关联需求**: FSD §7.15

### TC-TRADER-003: 删除 Trader — 有关联 Active 策略拒绝
- **前置条件**: Trader 有 Active 策略
- **测试步骤**:
  1. `DELETE /api/traders/:id`
- **预期结果**: 返回 `409 TRADER_HAS_ACTIVE_STRATEGIES`，提示需先停用所有策略
- **关联需求**: FSD §7.15, FSD §21.8

---

## M3 — 策略部署 (Strategy Deployments)

### TC-STRAT-DEPLOY-001: 创建策略部署 — 正常流程
- **前置条件**: 策略模板、Trader、Exchange 已存在
- **测试步骤**:
  1. `POST /api/traders/{traderId}/strategies` 传入 strategyId + exchangeId
- **预期结果**: 返回 201，状态为 Draft，scope 自动推导
- **关联需求**: FR-03.1, FSD §7.6.1

### TC-STRAT-DEPLOY-002: 创建策略部署 — Pair 作用域
- **前置条件**: 策略模板已存在
- **测试步骤**:
  1. `POST /api/traders/{traderId}/strategies` 传入 Pairs
- **预期结果**: scope = "Pair"
- **关联需求**: FSD §5.3.1

### TC-STRAT-DEPLOY-003: 创建策略部署 — Exchange 作用域
- **前置条件**: 策略模板已存在
- **测试步骤**:
  1. `POST /api/traders/{traderId}/strategies` 传入 exchangeId，Pairs 为空
- **预期结果**: scope = "Exchange"

### TC-STRAT-DEPLOY-004: 创建策略部署 — Trader 作用域
- **前置条件**: 策略模板已存在
- **测试步骤**:
  1. `POST /api/traders/{traderId}/strategies` exchangeId 和 Pairs 均为空
- **预期结果**: scope = "Trader"

### TC-STRAT-DEPLOY-005: 启用部署 — Active 状态
- **前置条件**: 部署状态非 Draft（如已有回测通过记录），无活跃冲突
- **测试步骤**:
  1. `POST /.../toggle` body: `{ "enable": true }`
- **预期结果**: 状态变为 Active
- **关联需求**: FSD §7.6.1

### TC-STRAT-DEPLOY-006: 启用部署 — Draft 状态被拒绝
- **前置条件**: 部署状态为 Draft
- **测试步骤**:
  1. `POST /.../toggle` body: `{ "enable": true }`
- **预期结果**: 返回 400，提示草稿不可启用

### TC-STRAT-DEPLOY-007: 部署启用 — Pair 冲突检测
- **前置条件**: 已有 Active 部署（Trader-1, Exchange-1, Pair-BTCUSDT）
- **测试步骤**:
  1. 尝试启用另一个同 Trader + 同 Exchange + 同 Pair 的部署
- **预期结果**: 返回 409 冲突
- **关联需求**: FR-03.2

### TC-STRAT-DEPLOY-008: 部署停用 — Active → Disabled
- **前置条件**: 部署为 Active
- **测试步骤**:
  1. `POST /.../toggle` body: `{ "enable": false }`
- **预期结果**: 状态变为 Disabled

### TC-STRAT-DEPLOY-009: 编辑部署 — Active 状态不可编辑
- **前置条件**: 部署为 Active
- **测试步骤**:
  1. `PUT /.../strategies/{id}` 修改 Pairs
- **预期结果**: 返回 400，提示活跃策略不可编辑

### TC-STRAT-DEPLOY-010: 删除部署 — Active 状态不可删除
- **前置条件**: 部署为 Active
- **测试步骤**:
  1. `DELETE /.../strategies/{id}`
- **预期结果**: 返回 400，提示活跃策略不可删除

---

## M2 — 交易所交易对与资产 (Exchange Pairs & Assets)

### TC-EXCH-SYM-001: 获取交易所交易对列表
- **前置条件**: Exchange 已配置且可连接
- **测试步骤**:
  1. `GET /api/exchanges/{id}/Pairs`
- **预期结果**: 返回含 USDT 交易对列表，含 Pair/price/priceChangePercent/volume 等实时行情
- **关联需求**: FR-01.5, FSD §7.5

### TC-EXCH-SYM-002: 获取交易所资产余额
- **前置条件**: Exchange 已配置且可连接
- **测试步骤**:
  1. `GET /api/exchanges/{id}/assets`
- **预期结果**: 返回资产余额列表，含 currency + balance
- **关联需求**: FSD §7.5

### TC-EXCH-SYM-003: 获取交易所侧订单
- **前置条件**: Exchange 已配置
- **测试步骤**:
  1. `GET /api/exchanges/{id}/orders?type=open`
  2. `GET /api/exchanges/{id}/orders?type=history`
- **预期结果**: 返回交易所侧实时订单列表
- **关联需求**: FSD §7.5

### TC-EXCH-SYM-004: 交易所禁用时 API 拒绝
- **前置条件**: Exchange 状态为 Disabled
- **测试步骤**:
  1. `GET /api/exchanges/{id}/Pairs`
  2. `GET /api/exchanges/{id}/assets`
- **预期结果**: 返回 400，提示交易所已禁用

### TC-SETUP-001: 首次启动检测初始化状态
- **前置条件**: 数据库为空，无 Super Admin
- **测试步骤**:
  1. 启动后端服务
  2. 发送 `GET /api/setup/status`
- **预期结果**: 返回 `{ "isInitialized": false }`
- **关联需求**: FR-15.1, FSD §7.2

### TC-SETUP-002: 初始化后状态检测
- **前置条件**: 已完成系统初始化
- **测试步骤**:
  1. 发送 `GET /api/setup/status`
- **预期结果**: 返回 `{ "isInitialized": true }`
- **关联需求**: FSD §7.2

### TC-SETUP-003: 初始化向导 — 正常流程
- **前置条件**: 数据库为空，无 Super Admin
- **测试步骤**:
  1. 发送 `POST /api/setup/initialize`，附带有效参数（userName, password）
- **预期结果**: 返回 `204 No Content`
- **关联需求**: FR-15.3, FSD §7.2

### TC-SETUP-004: 重复初始化被拒绝
- **前置条件**: 系统已初始化
- **测试步骤**:
  1. 发送 `POST /api/setup/initialize`
- **预期结果**: 返回 `409 SETUP_ALREADY_INITIALIZED`
- **关联需求**: FR-15.4, FR-15.5

### TC-SETUP-005: 初始化参数非法
- **前置条件**: 数据库为空
- **测试步骤**:
  1. 发送 `POST /api/setup/initialize`，password 为空
  2. 发送 `POST /api/setup/initialize`，userName 少于 3 个字符
- **预期结果**: 返回 `400 SETUP_INVALID_INPUT`
- **关联需求**: FSD §7.2

### TC-SETUP-006: 未初始化时非 Setup API 返回 503
- **前置条件**: 数据库为空
- **测试步骤**:
  1. 发送 `GET /api/strategies`
- **预期结果**: 返回 `503 SYSTEM_NOT_INITIALIZED`
- **关联需求**: FR-15.1, FSD §19.3

### TC-SETUP-007: 未初始化时 Health 可访问
- **前置条件**: 数据库为空
- **测试步骤**:
  1. 发送 `GET /health`
- **预期结果**: 返回 `200`，status 为 `Ok` 或 `Degraded`
- **关联需求**: FR-15.1

---

## M1 — 用户与鉴权 (Auth / MFA / Casbin)

### TC-AUTH-001: 正常登录（MFA 已绑定）
- **前置条件**: 用户已存在，状态 Active，MFA 已绑定
- **测试步骤**:
  1. 发送 `POST /api/auth/login`，正确用户名+密码
- **预期结果**: 返回 `{ "mfaRequired": true, "mfaToken": "..." }`
- **关联需求**: FR-08.1, FSD §7.3

### TC-AUTH-002: 登录密码错误
- **前置条件**: 用户已存在
- **测试步骤**:
  1. 发送 `POST /api/auth/login`，错误密码
- **预期结果**: 返回 `401 AUTH_INVALID_CREDENTIALS`
- **关联需求**: FSD §7.3

### TC-AUTH-003: 登录时用户被禁用
- **前置条件**: 用户状态为 Disabled
- **测试步骤**:
  1. 发送 `POST /api/auth/login`，正确凭据
- **预期结果**: 返回 `403 AUTH_USER_DISABLED`
- **关联需求**: FSD §7.3

### TC-AUTH-004: 验证 MFA — 正确 TOTP
- **前置条件**: 已获取 mfaToken
- **测试步骤**:
  1. 发送 `POST /api/auth/verify-mfa`，传入正确 totpCode + mfaToken
- **预期结果**: 返回 `{ "accessToken": "...", "refreshToken": "...", "expiresIn": 1800, "role": "Admin" }`
- **关联需求**: FR-08.3, FSD §7.3

### TC-AUTH-005: 验证 MFA — 错误 TOTP
- **前置条件**: 已获取 mfaToken
- **测试步骤**:
  1. 发送 `POST /api/auth/verify-mfa`，传入错误 totpCode
- **预期结果**: 返回 `401 AUTH_MFA_INVALID_CODE`
- **关联需求**: FSD §7.3

### TC-AUTH-006: 验证 MFA — 使用恢复码
- **前置条件**: 已获取 mfaToken，用户有剩余恢复码
- **测试步骤**:
  1. 发送 `POST /api/auth/verify-mfa`，传入有效 recoveryCode
- **预期结果**: 登录成功，签发 JWT，该恢复码标记为已使用
- **关联需求**: FSD §7.3

### TC-AUTH-007: 恢复码重复使用被拒绝
- **前置条件**: 某恢复码已被使用
- **测试步骤**:
  1. 再次使用同一恢复码进行 MFA 验证
- **预期结果**: 返回 `401 AUTH_MFA_INVALID_CODE`
- **关联需求**: FR-08.4

### TC-AUTH-008: Refresh Token 正常刷新
- **前置条件**: 已登录持有 refreshToken
- **测试步骤**:
  1. 发送 `POST /api/auth/refresh`，传入当前 refreshToken
- **预期结果**: 返回新的 accessToken + refreshToken，旧 refreshToken 被吊销
- **关联需求**: FR-08.7 (nFR-04), FSD §7.3

### TC-AUTH-009: Refresh Token 过期/无效
- **前置条件**: refreshToken 已过期或伪造
- **测试步骤**:
  1. 发送 `POST /api/auth/refresh`，传入过期 token
- **预期结果**: 返回 `401 AUTH_REFRESH_TOKEN_INVALID`
- **关联需求**: FSD §7.3

### TC-AUTH-010: Refresh Token 轮换 — 旧 token 不可重复使用
- **前置条件**: refreshToken 已被使用轮换
- **测试步骤**:
  1. 再次使用旧 refreshToken 请求刷新
- **预期结果**: 返回 `401 AUTH_REFRESH_TOKEN_INVALID`
- **关联需求**: FSD §20

### TC-AUTH-011: 未登录请求受保护 API
- **前置条件**: 无 JWT Token
- **测试步骤**:
  1. 发送 `GET /api/strategies`，无 Authorization 头
- **预期结果**: 返回 `401`
- **关联需求**: FR-08.7

### TC-AUTH-012: JWT 过期后请求
- **前置条件**: accessToken 已过期
- **测试步骤**:
  1. 携带过期 accessToken 请求 API
- **预期结果**: 返回 `401`

### TC-AUTH-013: MFA 绑定 — 首次绑定流程
- **前置条件**: 用户状态 PendingMfa
- **测试步骤**:
  1. 用户登录后引导至 MFA 绑定页面
  2. 发送 `POST /api/auth/mfa/setup` → 系统生成 TOTP Secret + QR Code URI + 恢复码
  3. 用户扫码并输入 6 位 TOTP 码
  4. 发送 `POST /api/auth/mfa/verify`，body: `{"code": "123456"}`
- **预期结果**: 返回 8 个恢复码 + JWT，用户状态变为 Active
- **关联需求**: FR-08.3, FR-08.4, FSD §7.2 (MFA 注册流程)

### TC-AUTH-014: MFA 绑定 — 错误 TOTP
- **前置条件**: 用户状态 PendingMfa
- **测试步骤**:
  1. 发送 `POST /api/auth/mfa/verify`，传入错误 TOTP
- **预期结果**: 返回 `400 AUTH_MFA_INVALID_CODE`，用户仍为 PendingMfa
- **关联需求**: FSD §7.3

### TC-AUTH-015: MFA 失败次数锁定
- **前置条件**: 用户已绑定 MFA
- **测试步骤**:
  1. 连续 5 次输入错误 TOTP 码
- **预期结果**: 第 5 次失败后，MFA Token 被锁定 5 分钟
- **关联需求**: TAD §9.3

### TC-AUTH-016: Admin 创建用户 — 正常流程
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. 发送 `POST /api/users`，userName + password + role
- **预期结果**: 返回 `201` + User 对象（不含敏感字段），用户状态为 PendingMfa
- **关联需求**: FR-08.8, FSD §7.4

### TC-AUTH-017: Admin 创建用户 — 用户名重复
- **前置条件**: 用户名已存在
- **测试步骤**:
  1. 发送 `POST /api/users`，已存在的 userName
- **预期结果**: 返回 `409 AUTH_USERNAME_EXISTS`
- **关联需求**: FSD §7.4

### TC-AUTH-018: Admin 创建用户 — 尝试创建 SuperAdmin 角色
- **前置条件**: Admin 登录
- **测试步骤**:
  1. 发送 `POST /api/users`，role = SuperAdmin
- **预期结果**: 返回 `400 AUTH_INVALID_ROLE`
- **关联需求**: FSD §7.4

### TC-AUTH-019: Admin 创建用户 — 不可分配 Admin 角色
- **前置条件**: Admin 登录
- **测试步骤**:
  1. 发送 `POST /api/users`，role = Admin
- **预期结果**: 返回 `400 AUTH_INVALID_ROLE`
- **关联需求**: FSD §7.4

### TC-AUTH-020: Super Admin 变更用户角色
- **前置条件**: Super Admin 已登录
- **测试步骤**:
  1. 发送 `PUT /api/users/:id/role`，role = Operator
- **预期结果**: 返回 `200`
- **关联需求**: FSD §7.4

### TC-AUTH-021: Super Admin 不可降级
- **前置条件**: Super Admin 已登录
- **测试步骤**:
  1. 发送 `PUT /api/users/:id/role`，目标为 Super Admin 自己
- **预期结果**: 返回 `400` 错误
- **关联需求**: FR-08.6, FSD §7.4

### TC-AUTH-022: Admin 禁用用户
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. 发送 `PUT /api/users/:id/status`，status = Disabled
- **预期结果**: 返回 `200`，用户状态变为 Disabled，不可登录
- **关联需求**: FSD §7.4

### TC-AUTH-023: Super Admin 不可禁用
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. 尝试禁用 Super Admin 用户
- **预期结果**: 返回 `400` 错误
- **关联需求**: FSD §7.4

### TC-AUTH-024: Admin 发送恢复码
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. 发送 `POST /api/auth/send-recovery-codes` + userId
- **预期结果**: 返回新恢复码列表，旧恢复码立即失效
- **关联需求**: FSD §7.3

### TC-AUTH-025: 密码 bcrypt 哈希存储验证
- **前置条件**: 创建用户
- **测试步骤**:
  1. 直接查询数据库 PasswordHash 字段
- **预期结果**: 存储的是 bcrypt 哈希（非明文，非 base64 编码的原始密码）
- **关联需求**: FR-08.2

### TC-AUTH-026: Viewer 访问 POST API
- **前置条件**: Viewer 已登录
- **测试步骤**:
  1. 发送 `POST /api/strategies`
- **预期结果**: 返回 `403 AUTH_FORBIDDEN`
- **关联需求**: FR-08.5, AC-05

### TC-AUTH-027: Viewer 访问 PUT API
- **前置条件**: Viewer 已登录
- **测试步骤**:
  1. 发送 `PUT /api/strategies/:id`
- **预期结果**: 返回 `403 AUTH_FORBIDDEN`

### TC-AUTH-028: Viewer 访问 DELETE API
- **前置条件**: Viewer 已登录
- **测试步骤**:
  1. 发送 `DELETE /api/strategies/:id`
- **预期结果**: 返回 `403 AUTH_FORBIDDEN`

### TC-AUTH-029: Viewer 访问 GET API
- **前置条件**: Viewer 已登录
- **测试步骤**:
  1. 发送 `GET /api/strategies`
  2. 发送 `GET /api/positions`
  3. 发送 `GET /api/orders`
- **预期结果**: 返回 `200` 成功
- **关联需求**: FSD §19.2

### TC-AUTH-030: Operator 权限验证 — 策略 CRUD
- **前置条件**: Operator 已登录
- **测试步骤**:
  1. `POST /api/strategies` → 201
  2. `PUT /api/strategies/:id` → 200
  3. `GET /api/strategies` → 200
  4. `DELETE /api/strategies/:id` → 204
  5. `POST /api/strategies/:id/toggle` → 200
- **预期结果**: 所有操作按预期成功
- **关联需求**: FSD §19.2

### TC-AUTH-031: Operator 权限验证 — 用户管理被拒绝
- **前置条件**: Operator 已登录
- **测试步骤**:
  1. `GET /api/users` → 403
  2. `POST /api/users` → 403
- **预期结果**: 返回 `403 AUTH_FORBIDDEN`
- **关联需求**: FSD §19.2

### TC-AUTH-032: Admin 权限验证 — 交易所管理
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. `GET /api/exchanges` → 200
  2. `POST /api/exchanges` → 201
  3. `PUT /api/exchanges/:id` → 200
  4. `DELETE /api/exchanges/:id` → 204
- **预期结果**: 所有操作成功
- **关联需求**: FSD §19.2

### TC-AUTH-033: Casbin 策略热加载验证
- **前置条件**: 系统运行中
- **测试步骤**:
  1. 修改 Casbin 策略文件（如移除某角色权限）
  2. 发送对应 API 请求
- **预期结果**: 权限变更立即生效，无需重启
- **关联需求**: TAD ADR-003

### TC-AUTH-034: Casbin 路径模式匹配
- **前置条件**: Operator 已登录
- **测试步骤**:
  1. 访问 `/api/strategies/123` GET
  2. 访问 `/api/strategies/123/toggle` POST
- **预期结果**: 路径模式 `keyMatch3` 正确匹配
- **关联需求**: FSD §19.1

### TC-AUTH-035: MFA Token 过期
- **前置条件**: 已获取 mfaToken，等待超过 5 分钟
- **测试步骤**:
  1. 使用过期 mfaToken 发送 verify-mfa 请求
- **预期结果**: 返回 `401`
- **关联需求**: FSD §20

---

## M1 — 系统设置 (System Settings)

> 覆盖 FR-11.2、FSD §7.13。系统级配置的查看与修改。

### TC-SETTINGS-001: 获取所有系统设置
- **前置条件**: Admin 已登录，系统已初始化
- **测试步骤**:
  1. `GET /api/settings`
- **预期结果**: 返回 200，body 包含 key-value 数组（如 jwt.access_token_expires_minutes, risk.default_slippage_percent 等）
- **关联需求**: FR-11.2, FSD §7.13

### TC-SETTINGS-002: 获取设置 — 非 Admin 拒绝
- **前置条件**: Trader 角色已登录
- **测试步骤**:
  1. `GET /api/settings`
- **预期结果**: 返回 403 Forbidden（Casbin 拒绝）
- **关联需求**: FSD §7.13, FSD §19

### TC-SETTINGS-003: 更新系统设置
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. `PUT /api/settings` body: `{ "settings": [ { "key": "risk.default_slippage_percent", "value": "0.2" } ] }`
- **预期结果**: 返回 200，设置已更新
  2. `GET /api/settings` 验证
- **预期结果**: 新值 `0.2` 已生效
- **关联需求**: FR-11.2, FSD §7.13

### TC-SETTINGS-004: 更新受保护 key 被拒绝
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. `PUT /api/settings` body: `{ "settings": [ { "key": "jwt.secret", "value": "newsecret" } ] }`
- **预期结果**: 返回 400，错误信息"key jwt.secret is read-only"
- **关联需求**: FSD §7.13

### TC-SETTINGS-005: 更新设置触发审计日志
- **前置条件**: Admin 已登录，审计日志正常
- **测试步骤**:
  1. `PUT /api/settings` 更新一个有效 key
  2. 查询审计日志
- **预期结果**: 审计日志记录 `settings.updated` 操作，含变更 key 和操作人
- **关联需求**: FSD §22

### TC-SETTINGS-006: 更新设置 — 不存在的 key
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. `PUT /api/settings` body: `{ "settings": [ { "key": "nonexistent.key", "value": "test" } ] }`
- **预期结果**: 返回 400，错误信息"unknown settings key"
- **关联需求**: FSD §7.13

### TC-SETTINGS-007: 系统设置热加载 — 风控参数即时生效
- **前置条件**: Admin 已登录，风控日亏损限额 = 1000
- **测试步骤**:
  1. `PUT /api/settings` 将 `risk.daily_loss_limit` 改为 `500`
  2. 触发亏损场景（当前亏损 600）
- **预期结果**: 新限额立即生效，拦截交易
- **关联需求**: FSD §7.13, FSD §9.5

### TC-SETTINGS-008: 并发设置更新 — 最终一致性
- **前置条件**: Admin 已登录
- **测试步骤**:
  1. 同时发送两个 `PUT /api/settings` 请求（不同 key）
- **预期结果**: 两个更新均成功，最终值均为最新提交值
- **关联需求**: FSD §7.13

### TC-SETTINGS-009: 设置并发写冲突 — 乐观锁保护
- **前置条件**: Admin 已登录，同一设置的 version = 5
- **测试步骤**:
  1. 两个 Admin 同时读取 settings（均获得 version = 5）
  2. Admin A 先提交 PUT（version = 5 → 6）
  3. Admin B 提交 PUT（version = 5 → 与当前版本 6 冲突）
- **预期结果**: Admin B 返回 409 Conflict，提示"setting has been modified by another user"
- **关联需求**: FSD §7.13, TAD §8.3

---

## M2 — 交易所管理 (Exchange)

### TC-EXCH-001: 创建交易所 — 正常流程
- **前置条件**: Admin/Operator 已登录
- **测试步骤**:
  1. 发送 `POST /api/exchanges`，body: `{ name, exchangeType, apiKey, secretKey }`
- **预期结果**: 返回 `201` + Exchange 对象（脱敏，不含 API Key）
- **关联需求**: FR-02.1, FR-02.2, FSD §7.5

### TC-EXCH-002: 创建交易所 — API Key AES 加密存储
- **前置条件**: 交易所已创建
- **测试步骤**:
  1. 读取数据库 Exchange 表的 ApiKeyEncrypted 字段
- **预期结果**: 存储值为 AES-256-GCM 密文，非明文
- **关联需求**: FR-02.3, TAD ADR-008

### TC-EXCH-003: 更新交易所配置
- **前置条件**: 交易所已存在
- **测试步骤**:
  1. 发送 `PUT /api/exchanges/:id`，更新 name
- **预期结果**: 返回 `200`，name 更新成功
- **关联需求**: FR-02.1, FSD §7.5

### TC-EXCH-004: 更新交易所 — 更新 API Key
- **前置条件**: 交易所已存在
- **测试步骤**:
  1. 发送 `PUT /api/exchanges/:id`，更新 apiKey
- **预期结果**: 新 Key 加密后入库，旧 Key 不可逆丢失
- **关联需求**: FR-02.3

### TC-EXCH-005: 删除交易所
- **前置条件**: 交易所无关联 Active 策略
- **测试步骤**:
  1. 发送 `DELETE /api/exchanges/:id`
- **预期结果**: 返回 `204 No Content`
- **关联需求**: FR-02.1, FSD §7.5

### TC-EXCH-006: 删除有关联 Active 策略的交易所被拒绝
- **前置条件**: 交易所存在关联的 Active 策略
- **测试步骤**:
  1. 发送 `DELETE /api/exchanges/:id`
- **预期结果**: 返回 `409 EXCHANGE_HAS_ACTIVE_STRATEGIES`
- **关联需求**: FSD §7.5

### TC-EXCH-007: 交易所不存在返回 404
- **前置条件**: 无
- **测试步骤**:
  1. 对不存在的 ID 发送 `PUT /api/exchanges/:id`
- **预期结果**: 返回 `404 EXCHANGE_NOT_FOUND`
- **关联需求**: FSD §7.5

### TC-EXCH-008: 测试连接 — 成功
- **前置条件**: 交易所配置有效
- **测试步骤**:
  1. 发送 `POST /api/exchanges/:id/test`
- **预期结果**: 返回 `{ "connected": true, "error": null }`
- **关联需求**: FR-01.4, FR-02.5, FSD §7.5

### TC-EXCH-009: 测试连接 — API Key 权限不足
- **前置条件**: API Key 无现货交易权限
- **测试步骤**:
  1. 发送 `POST /api/exchanges/:id/test`
- **预期结果**: `success: true` 但 `permissions.spotTrade: false`，带有警告信息
- **关联需求**: FR-02.5

### TC-EXCH-010: 测试连接 — 提现权限未关闭警告
- **前置条件**: API Key 开启了提现权限
- **测试步骤**:
  1. 发送 `POST /api/exchanges/:id/test`
- **预期结果**: `permissions.withdrawDisabled: false`，带有安全警告
- **关联需求**: FR-02.5

### TC-EXCH-011: 测试连接 — 连接失败
- **前置条件**: API Key/Secret 无效
- **测试步骤**:
  1. 发送 `POST /api/exchanges/:id/test`
- **预期结果**: 返回 `400 EXCHANGE_TEST_FAILED`
- **关联需求**: FSD §7.5

### TC-EXCH-012: 获取交易规则 — 正常
- **前置条件**: 交易所已启用可连接
- **测试步骤**:
  1. 发送 `GET /api/exchanges/:id/rules`
- **预期结果**: 返回 `{ "data": [ { "Pair": "BTCUSDT", "pricePrecision": 2, ... } ], "cachedAtUtc": "..." }`
- **关联需求**: FR-01.5, FSD §7.5

### TC-EXCH-013: 获取交易规则 — 缓存命中
- **前置条件**: 上次拉取时间 < 60 秒
- **测试步骤**:
  1. 第二次连续请求 `GET /api/exchanges/:id/rules`
- **预期结果**: 返回缓存的规则数据，cachedAtUtc 不变
- **关联需求**: FSD §7.5

### TC-EXCH-014: 获取交易规则 — 缓存过期重新拉取
- **前置条件**: 上次拉取时间 >= 60 秒
- **测试步骤**:
  1. 请求 `GET /api/exchanges/:id/rules`
- **预期结果**: 重新从交易所实时拉取，cachedAtUtc 更新
- **关联需求**: FSD §7.5

### TC-EXCH-015: 获取交易规则 — 交易所不可用
- **前置条件**: 交易所 API 不可达
- **测试步骤**:
  1. 请求 `GET /api/exchanges/:id/rules`
- **预期结果**: 返回 `400 EXCHANGE_RULES_FETCH_FAILED`
- **关联需求**: FSD §7.5

### TC-EXCH-016: 交易所启用/禁用状态切换
- **前置条件**: 交易所为 Enabled
- **测试步骤**:
  1. 发送 `POST /api/exchanges/:id/toggle`，body: `{ "enable": false }`
- **预期结果**: 交易所设为 Disabled，停用关联策略
- **关联需求**: FR-02.4, FSD §7.5

### TC-EXCH-016-B: toggle 请求体缺失返回 400
- **前置条件**: 交易所已存在
- **测试步骤**:
  1. 发送 `POST /api/exchanges/:id/toggle`，无请求体
- **预期结果**: 返回 `400 VALIDATION_ERROR`
- **关联需求**: FSD §7.5

### TC-EXCH-017: 获取交易所列表 — 筛选/翻页
- **前置条件**: 存在多个交易所
- **测试步骤**:
  1. `GET /api/exchanges?type=Binance&status=Enabled`
  2. `GET /api/exchanges?page=1&pageSize=10`
- **预期结果**: 正确返回筛选和翻页结果
- **关联需求**: FSD §7.5

### TC-EXCH-018: 交易所列表返回脱敏数据
- **前置条件**: 存在交易所
- **测试步骤**:
  1. `GET /api/exchanges`
- **预期结果**: 响应中不包含 ApiKey/Secret/Passphrase 字段
- **关联需求**: FSD §7.5

### TC-EXCH-019: 交易所修改时验证 API Key 权限
- **前置条件**: 新增或修改 API Key
- **测试步骤**:
  1. 新增交易所时传入新 API Key
- **预期结果**: 系统主动验证 Key 的现货交易权限
- **关联需求**: FR-02.5

### TC-EXCH-020: 交易所 Passphrase 可选字段
- **前置条件**: 创建交易所
- **测试步骤**:
  1. 对需要 Passphrase 的交易所类型（如 OKX），传入 passphrase
  2. 对不需要 Passphrase 的类型（如 Binance），不传 passphrase
- **预期结果**: 两种场景均成功创建
- **关联需求**: FSD §6.4

### TC-EXCH-021: IExchangeClient 统一接口验证
- **前置条件**: 各交易所客户端实现
- **测试步骤**:
  1. 对 Binance/OKX/Gate.io/Bybit/HTX 各实现类逐一验证
  2. 检查是否完整实现 IExchangeClient 接口全部方法
- **预期结果**: 5 个交易所均实现全部接口方法（SubscribeKlinesAsync, GetKlinesAsync, GetOrderBookAsync, GetBalanceAsync, GetPositionsAsync, PlaceOrderAsync, CancelOrderAsync, GetOrderAsync, GetRecentOrdersAsync, TestConnectionAsync, GetPairRulesAsync）
- **关联需求**: FR-01.1, FR-01.2, FR-01.3, TAD §3.3.3

---

## M2 — 限流层 (Rate Limiter)

### TC-RATE-001: 正常请求 — 限流未触发
- **前置条件**: 限流器有可用 token
- **测试步骤**:
  1. 发送 API 请求到交易所
- **预期结果**: 请求正常通过
- **关联需求**: FR-01.6, TAD §12

### TC-RATE-002: 无可用 token — 等待后通过
- **前置条件**: 限流器 token 暂时耗尽
- **测试步骤**:
  1. 连续发送请求直至 token 耗尽
  2. 在排队超时时间内等待
- **预期结果**: token 补充后请求通过
- **关联需求**: TAD §12.2

### TC-RATE-003: 无可用 token — 超时快速失败
- **前置条件**: 限流器 token 持续耗尽
- **测试步骤**:
  1. 持续发送请求，模拟高并发
  2. 排队等待超过 2s 超时
- **预期结果**: 快速失败并记录告警日志
- **关联需求**: TAD §12.2

### TC-RATE-004: 限流粒度隔离
- **前置条件**: 多个交易所实例
- **测试步骤**:
  1. 对一个交易所实例限流耗尽
  2. 向另一个交易所实例发送请求
- **预期结果**: 不同交易所/账户的限流互不影响
- **关联需求**: TAD §12.1

### TC-RATE-005: 限流参数配置化
- **前置条件**: 限流器已配置
- **测试步骤**:
  1. 修改 ExchangeRateLimitSettings 配置
  2. 重启后验证新限流参数生效
- **预期结果**: 限流行为按新参数执行
- **关联需求**: FR-01.6

---

## M3 — 策略与条件树 (Strategy)

### TC-STRAT-001: 创建策略模板 — 正常流程
- **前置条件**: 无
- **测试步骤**:
  1. 发送 `POST /api/strategies`，附带完整参数（name, entryCondition, exitCondition, executionRule）
- **预期结果**: 返回 `201` + Strategy 对象，status = Draft（不含 ExchangeId、Pairs、Timeframe）
- **关联需求**: FR-03.1, FR-03.3, FSD §7.6

### TC-STRAT-002: 创建策略 — 缺少必需字段
- **前置条件**: 无
- **测试步骤**:
  1. 发送 `POST /api/strategies`，缺少 name
- **预期结果**: 返回 `400 VALIDATION_ERROR`
- **关联需求**: FSD §7.6

### TC-STRAT-003: 更新策略
- **前置条件**: 策略为 Draft 或 Passed 状态
- **测试步骤**:
  1. 发送 `PUT /api/strategies/:id`，修改 entryCondition
- **预期结果**: 返回 `200`，策略状态回退为 Draft
- **关联需求**: FR-03.8, FSD §7.6

### TC-STRAT-004: 更新策略 — Active 状态不可编辑
- **前置条件**: 策略为 Active 状态
- **测试步骤**:
  1. 发送 `PUT /api/strategies/:id`
- **预期结果**: 返回 `400 STRATEGY_NOT_EDITABLE`
- **关联需求**: FSD §7.6

### TC-STRAT-005: 删除策略
- **前置条件**: 策略非 Active
- **测试步骤**:
  1. 发送 `DELETE /api/strategies/:id`
- **预期结果**: 返回 `204 No Content`
- **关联需求**: FSD §7.6

### TC-STRAT-006: 删除 Active 策略被拒绝
- **前置条件**: 策略为 Active
- **测试步骤**:
  1. 发送 `DELETE /api/strategies/:id`
- **预期结果**: 返回 `409 STRATEGY_IS_ACTIVE`
- **关联需求**: FSD §7.6

### TC-STRAT-007: 策略启用 — Passed → Active
- **前置条件**: 策略状态为 Passed，无活跃策略冲突
- **测试步骤**:
  1. 发送 `POST /api/strategies/:id/toggle`，action = enable
- **预期结果**: 策略状态变为 Active
- **关联需求**: FR-03.8, FSD §5.3, FSD §7.6

### TC-STRAT-008: 策略启用 — Draft 状态被拒绝
- **前置条件**: 策略状态为 Draft
- **测试步骤**:
  1. 发送 `POST /api/strategies/:id/toggle`，action = enable
- **预期结果**: 返回 `400 STRATEGY_CANNOT_ENABLE`
- **关联需求**: FSD §7.6

### TC-STRAT-009: 策略停用 — Active → Disabled
- **前置条件**: 策略为 Active
- **测试步骤**:
  1. 发送 `POST /api/strategies/:id/toggle`，action = disable
- **预期结果**: 策略状态变为 Disabled
- **关联需求**: FSD §5.3, FSD §7.6

### TC-STRAT-010: 策略状态机完整流转
- **前置条件**: 策略为 Draft
- **测试步骤**:
  1. Draft → 提交回测 → Backtesting
  2. Backtesting → 回测通过 → Passed
  3. Passed → 启用 → Active
  4. Active → 停用 → Disabled
  5. Disabled → 启用 → Active
  6. Active → 停用 → Disabled
  7. Disabled → 提交回测 → Backtesting（后续回测不改变状态）
  8. Backtesting → 取消 → Draft
- **预期结果**: 各状态转换符合 FSD §5.3 状态机定义
- **关联需求**: FR-03.8, FSD §5.3

### TC-STRAT-031: 回测中取消 — Backtesting → Draft
- **前置条件**: 策略状态为 Backtesting
- **测试步骤**:
  1. 用户取消回测任务
- **预期结果**: 策略状态回退为 Draft，回测任务状态为 Cancelled，不触发通过检查
- **关联需求**: FSD §5.3

### TC-STRAT-011: 活跃策略冲突检测 — 同 Trader + 同 Exchange + 同 Pair
- **前置条件**: 已有 Active 策略 A（Trader-1, Exchange-1, Pair-BTCUSDT）
- **测试步骤**:
  1. 尝试启用策略 B（同样 Trader-1, Exchange-1, Pair-BTCUSDT, Passed 状态）
- **预期结果**: 返回 `409 STRATEGY_EXCHANGE_CONFLICT`
- **关联需求**: FR-03.2, AC-08, FSD §7.6

### TC-STRAT-012: 活跃策略冲突 — 不同 Pair 可共存
- **前置条件**: 已有 Active 策略 A（Trader-1, Exchange-1, Pair-BTCUSDT）
- **测试步骤**:
  1. 启用策略 B（Trader-1, Exchange-1, Pair-ETHUSDT, Passed 状态）
- **预期结果**: 策略 B 成功启用为 Active
- **关联需求**: FR-03.2

### TC-STRAT-013: 活跃策略冲突 — 不同 Trader 可共存
- **前置条件**: 已有 Active 策略 A（Trader-1）
- **测试步骤**:
  1. 启用策略 B（Trader-2, 同 Exchange, 同 Pair）
- **预期结果**: 成功启用（不同 Trader 不冲突）
- **关联需求**: FR-03.2

### TC-STRAT-014: 获取策略列表 — 筛选/翻页
- **前置条件**: 存在多个策略
- **测试步骤**:
  1. `GET /api/strategies?status=Active&traderId=guid`
  2. `GET /api/strategies?page=2&pageSize=5`
- **预期结果**: 正确返回筛选和翻页结果
- **关联需求**: FSD §7.6

### TC-STRAT-015: 获取策略详情
- **前置条件**: 策略已存在
- **测试步骤**:
  1. `GET /api/strategies/:id`
- **预期结果**: 返回完整 Strategy 对象（含条件树 JSON、执行规则 JSON）
- **关联需求**: FSD §7.6

### TC-STRAT-016: K 线预热失败导致策略无法激活
- **前置条件**: 策略为 Passed，但交易所 K 线拉取失败
- **测试步骤**:
  1. 启用策略
- **预期结果**: 返回 `400 STRATEGY_KLINE_WARMUP_FAILED`，策略保持 Passed
- **关联需求**: FR-03.9, FSD §15.2

### TC-STRAT-017: 策略乐观并发控制
- **前置条件**: 策略当前版本号为 V1
- **测试步骤**:
  1. 两个并发请求同时更新同一策略
  2. 第二个请求因 Version 冲突失败
- **预期结果**: 仅一个请求成功，另一个返回并发冲突错误
- **关联需求**: TAD §8.3

### TC-STRAT-018: 策略执行规则验证 — 入场金额
- **前置条件**: 创建/更新策略
- **测试步骤**:
  1. `entryAmountType` = `fixed`，value = 100
  2. `entryAmountType` = `percent`，value = 50
  3. `entryAmountType` = `fixed`，value = -1（负数）
- **预期结果**: 前两种验证通过，负数返回 `400 VALIDATION_ERROR`
- **关联需求**: FR-03.6

### TC-STRAT-019: 策略执行规则验证 — 滑点容差
- **前置条件**: 创建/更新策略
- **测试步骤**:
  1. `slippageTolerancePercent` = 0.1（正常值）
  2. `slippageTolerancePercent` = -0.1（负值）
  3. `slippageTolerancePercent` = 0（零值）
- **预期结果**: 正常值通过，负值返回验证错误，零值视为无滑点控制
- **关联需求**: FR-06.8, FSD §6.6

### TC-STRAT-020: 策略执行规则验证 — 冷却期
- **前置条件**: 创建/更新策略
- **测试步骤**:
  1. `cooldownSeconds` = 300（正常值）
  2. `cooldownSeconds` = 0（无冷却）
  3. `cooldownSeconds` = -10（负数）
- **预期结果**: 0 和正数通过，负数返回验证错误
- **关联需求**: FR-07.4

### TC-STRAT-021: 策略状态机 — 进程意外中断
- **前置条件**: 策略评估周期进行中
- **测试步骤**:
  1. 模拟进程在评估周期中断
  2. 重启后检查策略状态
- **预期结果**: 该次评估视为未执行，策略状态保持为中断前状态
- **关联需求**: FR-03.10

### TC-STRAT-022: 条件树 — 嵌套 AND/OR/NOT 正常评估
- **前置条件**: 策略条件树包含多级嵌套
- **测试步骤**:
  1. 构造条件树: `AND(OR(RSI>70, RSI<30), NOT(SMA>50000))`
  2. 传入对应 K 线数据触发条件
- **预期结果**: 条件树正确递归评估
- **关联需求**: FR-03.4, TAD §5.4

### TC-STRAT-023: 条件树 — 深度超过 5 层被拒绝
- **前置条件**: 创建策略
- **测试步骤**:
  1. 传入深度为 6 层的条件树
- **预期结果**: 返回验证错误，树深度超限
- **关联需求**: TAD §5.4 约束规则 #1

### TC-STRAT-024: 条件树 — Not 节点必须有且仅 1 个子节点
- **前置条件**: 创建策略
- **测试步骤**:
  1. 传入 Not 节点含 0 个子节点
  2. 传入 Not 节点含 2 个子节点
- **预期结果**: 均返回验证错误
- **关联需求**: TAD §5.4 约束规则 #2

### TC-STRAT-025: 条件树 — Indicator 节点必须为叶节点
- **前置条件**: 创建策略
- **测试步骤**:
  1. 传入 Indicator 节点含子节点
- **预期结果**: 返回验证错误
- **关联需求**: TAD §5.4 约束规则 #3

### TC-STRAT-026: 条件树 — CrossAbove/CrossBelow 仅支持常数值
- **前置条件**: 创建策略
- **测试步骤**:
  1. 传入 CrossAbove 运算符，value 为指标引用（嵌套对象）
- **预期结果**: 返回验证错误
- **关联需求**: TAD §5.4 约束规则 #4

### TC-STRAT-027: 条件树 — offset 取值校验
- **前置条件**: 创建策略
- **测试步骤**:
  1. offset = 0（当前未闭合 K 线）— 允许
  2. offset = 1（上一根已闭合）— 允许
  3. offset = -1（出场条件允许）— 允许
  4. offset = 2（超出范围）— 拒绝
- **预期结果**: 0/1/-1 通过，2 拒绝
- **关联需求**: TAD §5.4 约束规则 #6

### TC-STRAT-028: 策略与批量 Pair 绑定
- **前置条件**: 创建策略
- **测试步骤**:
  1. 传入多个 Pairs
- **预期结果**: 策略成功关联多个交易对
- **关联需求**: FR-03.1

### TC-STRAT-029: 策略时间周期绑定
- **前置条件**: 创建策略
- **测试步骤**:
  1. 传入 timeframe = 15m
  2. 传入 timeframe = 1h
  3. 传入 timeframe = 2h（未支持的周期）
- **预期结果**: 1m/5m/15m/1h 通过，2h 返回验证错误
- **关联需求**: FR-03.7

### TC-STRAT-030: 策略执行规则 — 止损/止盈配置
- **前置条件**: 创建策略
- **测试步骤**:
  1. stopLossPercent = 5.0, takeProfitPercent = 10.0
  2. stopLossPercent = 0（无止损）
  3. stopLossPercent = -1（负值）
- **预期结果**: 非负值通过，负值拒绝
- **关联需求**: FR-07.1, FR-07.2

---

## M3 — 技术指标 (Indicators)

### TC-INDIC-001: SMA 计算验证
- **前置条件**: 提供 20 根 K 线数据
- **测试步骤**:
  1. 调用 `IIndicatorCalculator.Calculate()`，period = 5
- **预期结果**: 返回正确 SMA 值（最后 5 根 close 的算术平均）
- **关联需求**: FR-04.1, FR-04.2

### TC-INDIC-002: EMA 计算验证
- **前置条件**: 提供 30 根 K 线数据
- **测试步骤**:
  1. 调用 `IIndicatorCalculator.Calculate()`，period = 10
- **预期结果**: 返回正确 EMA 值（指数加权移动平均）
- **关联需求**: FR-04.2

### TC-INDIC-003: MACD 计算验证
- **前置条件**: 提供 50 根 K 线数据
- **测试步骤**:
  1. 调用 `IIndicatorCalculator.Calculate()`，fast=12, slow=26, signal=9
- **预期结果**: 返回 MACD 线、信号线、柱状图，与 Skender 库计算结果一致
- **关联需求**: FR-04.2

### TC-INDIC-004: RSI 计算验证
- **前置条件**: 提供 20 根 K 线数据
- **测试步骤**:
  1. 调用 `IIndicatorCalculator.Calculate()`，period = 14
- **预期结果**: 返回 RSI 值（0-100 之间），典型超买 > 70，超卖 < 30
- **关联需求**: FR-04.2

### TC-INDIC-005: Bollinger Bands 计算验证
- **前置条件**: 提供 30 根 K 线数据
- **测试步骤**:
  1. 调用 `IIndicatorCalculator.Calculate()`，period = 20, k = 2
- **预期结果**: 返回上轨/中轨/下轨，中轨 = SMA(20)
- **关联需求**: FR-04.2

### TC-INDIC-006: KDJ (Stochastic) 计算验证
- **前置条件**: 提供 20 根 K 线数据
- **测试步骤**:
  1. 调用 `IIndicatorCalculator.Calculate()`，k=5, d=3
- **预期结果**: 返回 %K/%D/%J 值，范围 0-100
- **关联需求**: FR-04.2

### TC-INDIC-007: Volume SMA 计算验证
- **前置条件**: 提供 30 根 K 线成交量数据
- **测试步骤**:
  1. 调用 `IIndicatorCalculator.Calculate()`，period = 20
- **预期结果**: 返回成交量移动平均值
- **关联需求**: FR-04.2

### TC-INDIC-008: 指标 Warmup 不足返回空
- **前置条件**: K 线数据量 < 指标需要的 warmup period
- **测试步骤**:
  1. SMA(200) 但只有 50 根 K 线
- **预期结果**: 返回空结果或标记为未就绪
- **关联需求**: TAD §3.3.7

### TC-INDIC-009: 多 K 线周期指标计算
- **前置条件**: 提供 1h 和 15m 两套 K 线数据
- **测试步骤**:
  1. 对同一指标在不同周期上分别计算
- **预期结果**: 计算结果基于各自周期的 K 线数据，互不影响
- **关联需求**: FR-04.4

### TC-INDIC-010: 指标注册表映射
- **前置条件**: TradeX.Indicators 已初始化
- **测试步骤**:
  1. 通过 IndicatorRegistry 按名称获取各指标
- **预期结果**: "SMA" → SMA Calculator, "RSI" → RSI Calculator, 非法名称 → null/异常
- **关联需求**: TAD §3.3.7

---

## M3 — 实时数据管道 (Data Pipeline)

### TC-DATA-001: WebSocket 行情数据接收
- **前置条件**: 交易所 WebSocket 已连接
- **测试步骤**:
  1. 模拟交易所推送 ticker/kline 数据
- **预期结果**: WebSocket Manager 正确接收并解析数据
- **关联需求**: FR-05.1, FR-05.2

### TC-DATA-002: WebSocket 数据写入内存缓存
- **前置条件**: 真实或模拟的 WS 数据流
- **测试步骤**:
  1. 接收 K 线数据后检查内存缓存
- **预期结果**: 内存缓存中包含最新 K 线数据
- **关联需求**: FR-05.3

### TC-DATA-005: WS 断线自动重连
- **前置条件**: WebSocket 已连接
- **测试步骤**:
  1. 模拟 WS 断线
  2. 观察重连行为
- **预期结果**: 触发指数退避重连（1s → 2s → 4s → ... → 30s max），重连成功后恢复数据流
- **关联需求**: FR-05.2, TAD §11

### TC-DATA-006: WS 断线后 K 线回填
- **前置条件**: WS 断线一段时间后重连成功
- **测试步骤**:
  1. WS 恢复连接
  2. 检查回填逻辑
- **预期结果**: 通过 REST API 回填缺失区间的 K 线数据到内存缓存（去重）
- **关联需求**: FR-13.4, TAD §5.3

### TC-DATA-007: WS 重连失败上限后标记 Disconnected
- **前置条件**: 交易所持续不可达
- **测试步骤**:
  1. WS 重连持续失败达到上限
- **预期结果**: 标记为 Disconnected，系统不崩溃，Health 端点反映该状态
- **关联需求**: TAD §8.1

### TC-DATA-008: 数据流向完整性验证
- **前置条件**: 完整系统运行
- **测试步骤**:
  1. 模拟交易所 WS 推送数据
  2. 追踪数据路径
- **预期结果**: 数据按 `WS → Memory Cache → Indicator → Strategy → SignalR` 路径正确流转
- **关联需求**: FR-05.5

### TC-DATA-009: 内存缓存并发读写
- **前置条件**: 多个策略同时运行
- **测试步骤**:
  1. 模拟大量 K 线数据并发写入和读取
- **预期结果**: 无竞态条件，无数据损坏
- **关联需求**: TAD §5.1

### TC-DATA-010: K 线预热 — 策略启用时触发
- **前置条件**: 策略状态 Passed → 尝试启用
- **测试步骤**:
  1. 执行 toggle enable
- **预期结果**: 自动从交易所 REST 拉取至少 3 天的 15 分钟级 K 线数据到内存缓存
- **关联需求**: FR-13.3, FSD §15.2

### TC-DATA-011: K 线预热 — 预热失败处理
- **前置条件**: 交易所 REST API 不可用
- **测试步骤**:
  1. 执行 toggle enable
- **预期结果**: 预热失败，策略不激活，返回明确错误提示
- **关联需求**: FR-13.3

### TC-DATA-013: 订单归档 — 正常触发
- **前置条件**: 存在超过 1 个月的订单
- **测试步骤**:
  1. 模拟每日 00:00 UTC 触发归档任务
- **预期结果**: 旧订单被归档为 JSON (gzip) 文件，SQLite 中保留摘要
- **关联需求**: FR-13.5, FSD §15.4

### TC-DATA-014: 订单导出 — CSV 格式
- **前置条件**: 存在订单数据
- **测试步骤**:
  1. `GET /api/orders/export?format=csv&startUtc=&endUtc=`
- **预期结果**: 返回 CSV 文件附件
- **关联需求**: FR-13.6, FSD §7.8

### TC-DATA-015: 订单导出 — JSON 格式
- **前置条件**: 存在订单数据
- **测试步骤**:
  1. `GET /api/orders/export?format=json`
- **预期结果**: 返回 JSON 文件附件
- **关联需求**: FR-13.6

### TC-DATA-016: 订单导出 — 时间范围限制
- **前置条件**: 无
- **测试步骤**:
  1. 请求超过 3 个月的数据范围
- **预期结果**: 仅返回最近 3 个月数据或返回错误提示
- **关联需求**: FSD §7.8

### TC-DATA-017: SignalR 推送 — StrategyStatusChanged
- **前置条件**: 策略状态变更触发
- **测试步骤**:
  1. 启用/停用策略
  2. 前端连接 SignalR
- **预期结果**: 前端收到 StrategyStatusChanged 事件，含新旧状态和变更原因
- **关联需求**: FR-11.13, TAD §17.2

### TC-DATA-018: SignalR 推送 — PositionUpdated
- **前置条件**: 持仓更新
- **测试步骤**:
  1. 模拟持仓数量/价格变化
- **预期结果**: 前端收到 PositionUpdated 事件
- **关联需求**: TAD §17.2

### TC-DATA-019: SignalR 推送 — OrderPlaced
- **前置条件**: 新订单产生
- **测试步骤**:
  1. 模拟下单成功
- **预期结果**: 前端收到 OrderPlaced 事件
- **关联需求**: TAD §17.2

### TC-DATA-020: SignalR 推送 — RiskAlert
- **前置条件**: 风控触发
- **测试步骤**:
  1. 模拟风控拦截
- **预期结果**: 前端收到 RiskAlert 事件（含 level/category/message）
- **关联需求**: TAD §17.2

### TC-DATA-021: SignalR 推送 — ExchangeConnectionChanged
- **前置条件**: 交易所连接状态变化
- **测试步骤**:
  1. 模拟 WS 断线/重连
- **预期结果**: 前端收到 ExchangeConnectionChanged 事件
- **关联需求**: TAD §17.2

### TC-DATA-022: SignalR 推送 — DashboardSummary
- **前置条件**: 系统运行中
- **测试步骤**:
  1. 等待下一评估周期
- **预期结果**: 每 15 秒前端收到 DashboardSummary 更新
- **关联需求**: FSD §17.2

### TC-DATA-023: 归档数据读取恢复
- **前置条件**: 旧订单已归档为 JSON 文件
- **测试步骤**:
  1. 用户查询超过 30 天的订单历史
  2. 系统读取归档 JSON 文件返回
- **预期结果**: 返回完整订单数据，格式与 SQLite 中的活跃订单一致，响应时间 < 2s
- **关联需求**: FR-13.5, FSD §15.4

---

## M4 — 交易执行 (Trading)

### TC-TRADE-001: 策略引擎评估循环 — 入场条件满足
- **前置条件**: 策略 Active，条件树配置完毕
- **测试步骤**:
  1. 推送满足入场条件的 K 线数据
  2. 等待评估周期触发
- **预期结果**: 入场条件评估为 true，进入风控链
- **关联需求**: FR-06.1, FR-06.2

### TC-TRADE-002: 策略引擎评估循环 — 出场条件满足
- **前置条件**: 持仓中，出场条件配置完毕
- **测试步骤**:
  1. 推送满足出场条件的 K 线数据
- **预期结果**: 出场条件评估为 true，执行卖出
- **关联需求**: FR-06.3

### TC-TRADE-003: 市价下单 — 正常执行
- **前置条件**: 风控全部通过
- **测试步骤**:
  1. Trading Engine 发起市价买单
- **预期结果**: 调用 IExchangeClient.PlaceOrderAsync，订单状态为 Pending/Filled
- **关联需求**: FR-06.4

### TC-TRADE-004: 限价下单 — 正常执行
- **前置条件**: 风控全部通过，策略配置限价单
- **测试步骤**:
  1. Trading Engine 发起限价买单
- **预期结果**: 调用 PlaceOrderAsync 限价单参数，订单状态为 Pending
- **关联需求**: FR-06.4

### TC-TRADE-005: 手动下单 — 正常执行
- **前置条件**: Admin/Operator 已登录，Trader 已存在
- **测试步骤**:
  1. 发送 `POST /api/traders/{traderId}/orders/manual`
- **预期结果**: 返回 `201` + Order 对象，isManual = true
- **关联需求**: FR-06.5, FSD §7.8

### TC-TRADE-006: 手动下单 — 风控拒绝
- **前置条件**: 日亏损已超限
- **测试步骤**:
  1. 发送 `POST /api/traders/{traderId}/orders/manual`
- **预期结果**: 返回 `400 ORDER_RISK_REJECTED`
- **关联需求**: FSD §7.8

### TC-TRADE-007: 手动下单 — 滑点超限拒绝
- **前置条件**: 预估滑点超过容差
- **测试步骤**:
  1. 发送 `POST /api/traders/{traderId}/orders/manual`
- **预期结果**: 返回 `400 ORDER_SLIPPAGE_EXCEEDED`
- **关联需求**: FR-06.8, FSD §7.8

### TC-TRADE-008: 下单结果写入 Order 记录
- **前置条件**: 下单成功
- **测试步骤**:
  1. 检查 Order 表
- **预期结果**: 新订单记录写入，含 exchangeOrderId, quantity, price, fee 等字段
- **关联需求**: FR-06.6

### TC-TRADE-009: 下单后持仓状态更新
- **前置条件**: 买单成交
- **测试步骤**:
  1. 检查 Position 表
- **预期结果**: 持仓记录创建（数量、均价、PnL）
- **关联需求**: FR-06.6

### TC-TRADE-010: 滑点控制 — 滑点未超限
- **前置条件**: 策略滑点容差 0.1%
- **测试步骤**:
  1. 模拟当前市场滑点 0.05%
- **预期结果**: 滑点检查通过，继续交易
- **关联需求**: FR-06.8

### TC-TRADE-011: 滑点控制 — 滑点超限拒绝
- **前置条件**: 策略滑点容差 0.1%
- **测试步骤**:
  1. 模拟当前市场滑点 0.5%
- **预期结果**: 滑点检查拒绝，记录日志，不下单
- **关联需求**: FR-06.8, AC-09

### TC-TRADE-012: 滑点控制 — 全局默认值
- **前置条件**: 策略未设置滑点容差
- **测试步骤**:
  1. 使用 `SystemConfig.risk.default_slippage_percent` 作为默认值
- **预期结果**: 正确读取并使用全局默认值
- **关联需求**: FR-06.8

### TC-TRADE-013: 余额不足拒绝
- **前置条件**: 账户余额不足
- **测试步骤**:
  1. 尝试下单超过可用余额
- **预期结果**: 返回 `400 ORDER_INSUFFICIENT_BALANCE`
- **关联需求**: FSD §21.6

### TC-TRADE-014: 交易所拒绝下单处理
- **前置条件**: 交易所返回错误
- **测试步骤**:
  1. 模拟交易所拒绝
- **预期结果**: 返回 `400 ORDER_EXCHANGE_REJECTED`，记录错误日志
- **关联需求**: FSD §21.6

### TC-TRADE-015: 订单状态机完整流转
- **前置条件**: 新订单创建
- **测试步骤**:
  1. Pending → PartiallyFilled → Filled
  2. Pending → Cancelled
  3. Pending → Failed
- **预期结果**: 各状态转换符合 FSD §5.4 定义
- **关联需求**: FSD §5.4

### TC-TRADE-016: Trading Engine 单策略异常隔离
- **前置条件**: 两个独立策略 A 和 B 同时运行
- **测试步骤**:
  1. 策略 A 的交易所连接断开（模拟异常）
  2. 观察 Trading Engine 行为
- **预期结果**: 策略 A 标记为 Error 状态并暂停，策略 B 正常运行不受影响，日志记录"Strategy A paused due to engine error"
- **关联需求**: TAD §8.1

### TC-TRADE-017: 手动下单与策略下单共用风控链
- **前置条件**: 日亏损额度 = 1000，当前亏损 = 800
- **测试步骤**:
  1. 策略下单 300 → 预期被风控拒绝（800 + 300 > 1000）
  2. 手动下单 200 → 同样被风控拒绝
- **预期结果**: 两条路径均返回 ORDER_RISK_REJECTED，共用同一风控额度计数器
- **关联需求**: TAD §8.3, FR-06.5

### TC-TRADE-018: 订单状态幂等更新
- **前置条件**: 订单状态为 PartiallyFilled
- **测试步骤**:
  1. 重复两次交易所成交回调（填充量为 0.5、0.5）
  2. 再次发送已到达状态的成交回调（Fill）
- **预期结果**: 第二次成交回调被忽略（幂等），最终状态稳定为 Filled，不抛出异常
- **关联需求**: TAD §8.3

---

## M4 — 风控 (Risk Control) — 策略级

### TC-RISK-001: 止损触发
- **前置条件**: 持仓中，止损位 5%
- **测试步骤**:
  1. 当前价格下跌超过 5%
  2. 评估周期触发
- **预期结果**: 触发止损卖出，记录日志，发送通知
- **关联需求**: FR-07.1, AC-10

### TC-RISK-002: 止盈触发
- **前置条件**: 持仓中，止盈位 10%
- **测试步骤**:
  1. 当前价格上涨超过 10%
- **预期结果**: 触发止盈卖出
- **关联需求**: FR-07.2, AC-10

### TC-RISK-003: 移动止损 — 价格上升后回调触发
- **前置条件**: 持仓中，移动止损 3% 已启用，价格从 100 → 110 → 105
- **测试步骤**:
  1. 价格从 110 跌至 105（跌幅 > 3% 从新高 110 算）
- **预期结果**: 移动止损触发，执行卖出（触发价 106.7 = 110 * 0.97）
- **关联需求**: FR-07.3

### TC-RISK-004: 移动止损 — 价格持续上涨不触发
- **前置条件**: 持仓中，移动止损 3%，价格从 100 → 110 → 120
- **测试步骤**:
  1. 持续监控
- **预期结果**: 移动止损不触发（止损线随价格上涨而抬高）
- **关联需求**: FR-07.3

### TC-RISK-005: 交易冷却期 — 冷却中跳过买入
- **前置条件**: 策略冷却期 300s，距上次交易 < 300s
- **测试步骤**:
  1. 入场条件满足
- **预期结果**: 冷却期检查拦截，跳过本次买入
- **关联需求**: FR-07.4

### TC-RISK-006: 交易冷却期 — 冷却期过后正常买入
- **前置条件**: 距上次交易 >= 300s
- **测试步骤**:
  1. 入场条件满足
- **预期结果**: 冷却期检查通过
- **关联需求**: FR-07.4

### TC-RISK-007: 最大持仓数限制 — 达到上限
- **前置条件**: 持仓数已达上限
- **测试步骤**:
  1. 入场条件满足
- **预期结果**: 最大持仓检查拦截，跳过买入
- **关联需求**: FR-07.5

### TC-RISK-008: 日亏损限额触发
- **前置条件**: 当日累计已实现亏损超过阈值（如 10%）
- **测试步骤**:
  1. 入场条件满足
- **预期结果**: 日亏损检查拦截，暂停全账户交易，通知发送
- **关联需求**: FR-07.6, AC-10

### TC-RISK-009: 日亏损次日 UTC 自动恢复
- **前置条件**: 日亏损暂停, 模拟时间跨越次日 00:00 UTC
- **测试步骤**:
  1. 检查次日交易是否恢复
- **预期结果**: 全账户交易在次日 UTC 自动恢复，无需手动干预
- **关联需求**: FR-07.6

### TC-RISK-010: 最大回撤暂停
- **前置条件**: 账户净值从峰值回撤超过阈值（如 25%）
- **测试步骤**:
  1. 入场条件满足
- **预期结果**: 最大回撤检查拦截，暂停所有交易
- **关联需求**: FR-07.7, AC-10

### TC-RISK-011: 连续亏损暂停策略
- **前置条件**: 策略连续亏损次数 >= 阈值（如 5）
- **测试步骤**:
  1. 入场条件满足
- **预期结果**: 连续亏损检查拦截，暂停该策略，通知发送
- **关联需求**: FR-07.8, AC-10

### TC-RISK-012: 交易频率熔断 — 5 分钟内触发 >= 3 次
- **前置条件**: 5 分钟内已触发 3 次买入信号
- **测试步骤**:
  1. 再触发一次买入信号
- **预期结果**: 熔断拦截，跳过本次，冷却期延长
- **关联需求**: FR-07.9

### TC-RISK-013: 风控链执行顺序验证
- **前置条件**: 各风控检查项均配置
- **测试步骤**:
  1. 按顺序执行 7 个检查项（滑点 → 日亏损 → 回撤 → 连续亏损 → 熔断 → 冷却期 → 最大持仓）
- **预期结果**: 严格按 FSD §9.1 定义顺序执行，任一拦截即短路
- **关联需求**: FSD §9.1, TAD §3.3.2

### TC-RISK-014: 风控链全部通过
- **前置条件**: 所有风控检查均符合条件
- **测试步骤**:
  1. 执行风控链
- **预期结果**: 全部通过，进入下单执行
- **关联需求**: FSD §9.2

### TC-RISK-015: 风控链短路 — 第一个检查即拦截
- **前置条件**: 滑点已超限
- **测试步骤**:
  1. 执行风控链
- **预期结果**: SlippageCheck 拦截，不继续后续检查
- **关联需求**: FSD §9.1

### TC-RISK-016: 风控检查日志记录
- **前置条件**: 风控链执行
- **测试步骤**:
  1. 模拟拦截场景
  2. 检查日志
- **预期结果**: 结构化日志记录 RiskContext 信息，含拦截原因
- **关联需求**: TAD §8.2

---

## M4 — 多层级组合风控 (Portfolio Risk)

### TC-RISK-020: 系统级 Kill Switch
- **前置条件**: 系统运行中，有活跃策略和挂单
- **测试步骤**:
  1. Admin 发送 `POST /api/system/emergency-stop`
- **预期结果**: Trading Engine 停止 → 所有交易所撤单 → 全部策略禁用 → 发送紧急通知
- **关联需求**: FR-07.10, AC-10-B, FSD §9.5.1

### TC-RISK-021: Kill Switch 后手动恢复
- **前置条件**: Kill Switch 已触发
- **测试步骤**:
  1. 手动启用策略
- **预期结果**: 策略可重新启用，Trading Engine 恢复运行
- **关联需求**: FSD §9.5.1

### TC-RISK-022: 系统总敞口上限触发
- **前置条件**: 所有持仓总市值超过系统级上限
- **测试步骤**:
  1. 发送买入信号
- **预期结果**: 系统级风控拦截，停止所有交易，发送紧急通知
- **关联需求**: FSD §9.5.1

### TC-RISK-017: 连续亏损阈值 — N 笔亏损后禁止交易
- **前置条件**: 策略已连续亏损 N-1 笔，N=配置阈值（默认 5）
- **测试步骤**:
  1. 第 N 笔交易以亏损平仓
  2. 尝试发送新交易信号
- **预期结果**: 风控拦截，策略标记为连续亏损状态，需人工或冷却期后恢复
- **关联需求**: FSD §9.5.3

### TC-RISK-018: 连续亏损冷却期自动解除
- **前置条件**: 策略因连续亏损被禁止，冷却期配置为 30 分钟
- **测试步骤**:
  1. 等待冷却期结束
  2. Trading Engine 重启或下一个周期触发状态检查
- **预期结果**: 策略自动解除禁止状态，可正常交易
- **关联需求**: FSD §9.5.3

### TC-RISK-019: 风控配置热加载
- **前置条件**: 系统运行中，有活跃策略
- **测试步骤**:
  1. Admin 修改风控参数（如日亏损限额从 1000 → 500）
  2. 发送超出新限额的信号
- **预期结果**: 新配置立即生效，拦截交易
- **关联需求**: FSD §9.5

---

## M4 — 多层级组合风控 (续)

### TC-RISK-023: 交易所级风控 — 单交易所敞口超限
- **前置条件**: 某交易所上持仓总价值超过该交易所限额
- **测试步骤**:
  1. 向该交易所发送买入信号
- **预期结果**: 交易所级风控拦截，仅阻止该交易所交易，不影响其他交易所
- **关联需求**: FSD §9.5.1

### TC-RISK-024: 交易所级风控 — 该交易所日交易次数超限
- **前置条件**: 某交易所当日交易次数已达上限
- **测试步骤**:
  1. 向该交易所发送任何信号
- **预期结果**: 风控拦截，日志记录"交易所日交易次数超限"
- **关联需求**: FSD §9.5.1

### TC-RISK-025: 交易员级风控 — 该交易员总敞口超限
- **前置条件**: 某交易员名下所有策略持仓总和超过限额
- **测试步骤**:
  1. 该交易员创建新策略并发出买入信号
- **预期结果**: 交易员级风控拦截
- **关联需求**: FSD §9.5.2

### TC-RISK-026: 交易员级风控 — 日亏损超限
- **前置条件**: 某交易员当日已亏损达到限额
- **测试步骤**:
  1. 该交易员名下任意策略尝试开仓
- **预期结果**: 拦截所有开仓，仅允许减仓/平仓操作
- **关联需求**: FSD §9.5.2

### TC-RISK-027: 多头/空头净敞口限制
- **前置条件**: 系统多头净敞口已达上限
- **测试步骤**:
  1. 发送买入信号（增加多头）
- **预期结果**: 净敞口风控拦截买入
  2. 发送卖出信号（增加空头或减少多头）
- **预期结果**: 允许执行
- **关联需求**: FSD §9.5.1

### TC-RISK-028: 行情异动熔断 — 瞬时跌幅超过阈值
- **前置条件**: 行情数据源推送正常
- **测试步骤**:
  1. 模拟某交易对价格 1 分钟内跌幅超过 5%
- **预期结果**: 熔断触发，暂停该交易对所有交易 5 分钟，发送告警通知
- **关联需求**: FSD §9.5.1

### TC-RISK-029: 熔断后自动恢复
- **前置条件**: 熔断已触发，暂停倒计时中
- **测试步骤**:
  1. 等待熔断倒计时结束
- **预期结果**: 该交易对自动恢复交易
- **关联需求**: FSD §9.5.1

### TC-RISK-030: 风控通知分级
- **前置条件**: 风控模块配置了通知渠道
- **测试步骤**:
  1. 触发"日亏损超限"（高优先级）
  2. 触发"交易次数接近上限"（低优先级）
- **预期结果**: 高优先级通知立即发送，低优先级通知合并/延时发送
- **关联需求**: TAD §8.2

### TC-RISK-031: 风控规则优先级 — 系统级 > 交易所级 > 交易员级 > 策略级
- **前置条件**: 系统级、交易所级、策略级均设置了不同限额
- **测试步骤**:
  1. 模拟系统级限额已超，但策略级和交易所级均未超限
  2. 发送买入信号
- **预期结果**: 系统级最先拦截，不继续检查下级规则
- **关联需求**: FSD §9.5

### TC-RISK-032: 风控规则可配置 — 启用/禁用单项规则
- **前置条件**: Admin 权限
- **测试步骤**:
  1. Admin 禁用"连续亏损检查"
  2. 触发连续亏损场景
- **预期结果**: 风控链跳过禁用项，交易正常执行
  3. Admin 重新启用
  4. 再次触发
- **预期结果**: 拦截
- **关联需求**: FSD §9.5.3

### TC-RISK-033: 风控性能 — 全量检查在 100ms 内完成
- **前置条件**: 所有风控规则启用，100 个活跃策略
- **测试步骤**:
  1. 发送交易信号
  2. 记录风控链执行耗时
- **预期结果**: 全部检查在 100ms 内返回结果（P99）
- **关联需求**: TAD §9.1

### TC-RISK-034: 风控降级 — 风控模块自身异常不影响交易执行
- **前置条件**: 风控模块数据库连接中断
- **测试步骤**:
  1. 发送交易信号
- **预期结果**: 风控模块返回降级结果（允许交易），日志记录降级原因
- **关联需求**: FSD §9.5.4

### TC-RISK-035: Kill Switch — 撤销所有挂单
- **前置条件**: 系统运行中，3 个交易所各有 5 笔挂单
- **测试步骤**:
  1. 触发 Kill Switch
- **预期结果**: 每个交易所的 `CancelAllOrdersAsync` 被调用，所有挂单被撤销
- **关联需求**: FR-07.10, AC-10-B, FSD §9.5.1

### TC-RISK-036: Kill Switch — 禁用所有策略
- **前置条件**: 5 个策略处于启用状态
- **测试步骤**:
  1. 触发 Kill Switch
- **预期结果**: 全部策略状态变为 Disabled
- **关联需求**: FR-07.10, FSD §9.5.1

### TC-RISK-037: Kill Switch — 停止 Trading Engine
- **前置条件**: TradingEngine BackgroundService 运行中
- **测试步骤**:
  1. 触发 Kill Switch
- **预期结果**: TradingEngine 的 `ExecuteAsync` 退出循环，服务状态标记为 Stopped
- **关联需求**: FR-07.10, FSD §9.5.1

### TC-RISK-038: Kill Switch — 发送紧急通知
- **前置条件**: 通知渠道已配置（Telegram/Email）
- **测试步骤**:
  1. 触发 Kill Switch
- **预期结果**: `INotificationService.SendEmergencyAsync` 被调用，紧急消息送达所有渠道
- **关联需求**: FR-07.10, FSD §9.5.1

### TC-RISK-039: Kill Switch — 审计日志记录
- **前置条件**: 系统运行中
- **测试步骤**:
  1. Admin 或自动触发 Kill Switch
- **预期结果**: 审计日志记录 `SystemEmergencyStop` 操作，含触发来源和时间戳
- **关联需求**: FSD §22, AC-10-B

### TC-RISK-040: Kill Switch — 幂等性（多次触发不重复执行）
- **前置条件**: Kill Switch 已触发
- **测试步骤**:
  1. 再次调用 Kill Switch
- **预期结果**: 第二次调用无副作用，日志记录"Kill Switch already triggered"
- **关联需求**: FSD §9.5.1

### TC-RISK-041: 滑点保护 — 实际滑点超过最大容忍值
- **前置条件**: 配置最大滑点 0.5%
- **测试步骤**:
  1. 下单时市场深度不足，实际成交滑点 0.8%
- **预期结果**: 订单被拒绝，不成交
- **关联需求**: FSD §9.5.3

### TC-RISK-042: 滑点保护 — 部分成交滑点保护
- **前置条件**: 大额订单，市场深度不足以一次性成交
- **测试步骤**:
  1. 下单后部分成交
  2. 剩余部分滑点超过阈值
- **预期结果**: 已成交部分保留，未成交部分取消
- **关联需求**: FSD §9.5.3

### TC-RISK-043: 风控统计重置 — 日统计数据跨日清零
- **前置条件**: 当日有交易记录，日亏损 800
- **测试步骤**:
  1. 系统时间跨过 00:00
- **预期结果**: 日交易次数、日盈亏等统计自动清零
- **关联需求**: FSD §9.5

### TC-RISK-044: 风控黑名单 — 禁止特定交易对交易
- **前置条件**: Admin 将 BTC_USDT 加入黑名单
- **测试步骤**:
  1. 策略发送 BTC_USDT 买入信号
- **预期结果**: 风控拦截，日志记录"交易对被黑名单禁止"
- **关联需求**: FSD §9.5.3

### TC-RISK-045: 风控白名单 — 仅允许特定策略交易
- **前置条件**: 系统设置为白名单模式，仅策略 A 在白名单中
- **测试步骤**:
  1. 策略 B 发送信号
- **预期结果**: 拦截
  2. 策略 A 发送信号
- **预期结果**: 允许
- **关联需求**: FSD §9.5.3

### TC-RISK-046: 最大持仓时间限制
- **前置条件**: 配置最大持仓时间 72 小时
- **测试步骤**:
  1. 持仓超过 72 小时
- **预期结果**: 风控触发强制平仓信号
- **关联需求**: FSD §9.5.3

### TC-RISK-047: 最小持仓时间限制
- **前置条件**: 配置最小持仓时间 5 分钟
- **测试步骤**:
  1. 开仓后 2 分钟发送平仓信号
- **预期结果**: 风控拦截平仓信号
  2. 开仓后 6 分钟发送平仓信号
- **预期结果**: 允许平仓
- **关联需求**: FSD §9.5.3

### TC-RISK-048: 风控告警阈值 — 接近限制时发送预警
- **前置条件**: 日亏损限额 1000，当前亏损 800（达到 80% 阈值）
- **测试步骤**:
  1. 下一笔交易亏损 50
- **预期结果**: 交易正常执行，但发送预警通知"日亏损已达限额的 85%"
- **关联需求**: FSD §9.5

### TC-RISK-049: 风控规则表达式引擎 — 自定义规则
- **前置条件**: 系统支持自定义风控规则表达式
- **测试步骤**:
  1. Admin 添加自定义规则 "position_size > 0.1 * balance"
  2. 发送仓位超过 10% 余额的信号
- **预期结果**: 自定义规则生效，拦截交易
- **关联需求**: FSD §9.5.3

### TC-RISK-050: 风控并发安全
- **前置条件**: 多线程同时触发交易信号
- **测试步骤**:
  1. 10 个并发任务同时调用风控检查
- **预期结果**: 竞态条件处理正确，统计数据准确（无超卖/超买）
- **关联需求**: TAD §8.1

---

## M5 — 回测引擎 (Backtest Engine)

> 覆盖 AC-16 (TC-BACKTEST-001 ~ 020)。对应 FR-12.x、FSD §14、TAD §10（IBacktestEngine 接口契约）。

### TC-BACKTEST-001: 回测任务创建 — 参数完整
- **前置条件**: 存在至少一个策略部署、一条数据源、一个交易所配置
- **测试步骤**:
  1. `POST /api/traders/{traderId}/strategies/{strategyId}/backtests?deploymentId=..&exchangeId=..&Pair=..&timeframe=..&startUtc=..&endUtc=..&initialCapital=1000`
- **预期结果**: 返回 200 + taskId + status(Pending)，策略状态变为 Backtesting
- **关联需求**: FR-12.1, FSD §7.9.1, FSD §14.1

### TC-BACKTEST-002: 回测任务创建 — 必填参数缺失
- **前置条件**: 无
- **测试步骤**:
  1. `POST /api/traders/{traderId}/strategies/{strategyId}/backtests` 缺少必要 query 参数
- **预期结果**: 返回 400，错误信息明确指明缺少参数
- **关联需求**: FSD §7.9.1

### TC-BACKTEST-003: 回测任务创建 — 策略不存在
- **前置条件**: strategyId 无效
- **测试步骤**:
  1. `POST /api/traders/{traderId}/strategies/{strategyId}/backtests` 使用不存在的 strategyId
- **预期结果**: 返回 400，错误信息
- **关联需求**: FSD §7.9.1

### TC-BACKTEST-004: 回测任务创建 — 时间范围无效
- **前置条件**: 无
- **测试步骤**:
  1. `POST /api/traders/{traderId}/strategies/{strategyId}/backtests` 中 startUtc > endUtc
- **预期结果**: 返回 400，错误信息
- **关联需求**: FSD §7.9.1

### TC-BACKTEST-005: 回测执行 — 正常完成
- **前置条件**: 回测任务状态为 Pending
- **测试步骤**:
  1. 等待系统调度执行
- **预期结果**: 状态变为 Running → Completed，result 包含总收益率、夏普比率、最大回撤、交易记录
- **关联需求**: FR-12.2, FSD §14.2

### TC-BACKTEST-006: 回测结果查询
- **前置条件**: 存在已完成回测任务
- **测试步骤**:
  1. `GET /api/traders/{traderId}/strategies/{strategyId}/backtests/tasks/{taskId}/result`
- **预期结果**: 返回完整回测结果，含 totalReturnPercent, sharpeRatio, maxDrawdown, winRate, totalTrades, trades
- **关联需求**: FSD §7.9.2

### TC-BACKTEST-007: 回测任务列表
- **前置条件**: 存在多条回测记录
- **测试步骤**:
  1. `GET /api/traders/{traderId}/strategies/{strategyId}/backtests/tasks`
- **预期结果**: 返回回测任务列表
- **关联需求**: FSD §7.9.2

### TC-BACKTEST-008: 回测任务详情
- **前置条件**: 回测任务存在
- **测试步骤**:
  1. `GET /api/traders/{traderId}/strategies/{strategyId}/backtests/tasks/{taskId}`
- **预期结果**: 返回任务详情，含 status/phase/时间范围
- **关联需求**: FSD §7.9.2

### TC-BACKTEST-013: 回测费用模型 — 不同费率对比
- **前置条件**: 创建两个费用模型：maker=0.1%/taker=0.1% vs maker=0.02%/taker=0.05%
- **测试步骤**:
  1. 同一策略、同一数据范围分别回测
- **预期结果**: 高费率回测的总收益率显著低于低费率回测，交易次数一致
- **关联需求**: FSD §14.1

### TC-BACKTEST-014: 回测滑点模型
- **前置条件**: 配置滑点模型 slippage=0.1%
- **测试步骤**:
  1. 回测包含多次买入卖出
- **预期结果**: 每笔成交价 = 信号价 * (1 ± 滑点)，且在最终统计中体现
- **关联需求**: FSD §14.1

### TC-BACKTEST-015: 回测结果与实盘对比
- **前置条件**: 同一策略有实盘交易记录和回测记录
- **测试步骤**:
  1. 比较两者的收益率、交易频率、胜率等关键指标
- **预期结果**: 关键指标趋势一致（偏差在可接受范围内）
- **关联需求**: FR-12.4, FSD §14.3

### TC-BACKTEST-016: 回测 — 30 天 15 分钟粒度数据在 10s 内完成
- **前置条件**: 策略中等复杂度，数据源为 15 分钟 K 线
- **测试步骤**:
  1. 创建回测任务，30 天数据范围
  2. 记录执行耗时
- **预期结果**: 回测在 10s 内完成（TAD §9.1 性能目标）
- **关联需求**: TAD §9.1

### TC-BACKTEST-017: 回测 — 并发执行限制
- **前置条件**: 系统配置最大并发回测数为 3
- **测试步骤**:
  1. 同时提交 5 个回测任务
- **预期结果**: 3 个立即进入 Running，2 个保持 Pending
  2. 其中一个 Running 完成后
- **预期结果**: 一个 Pending 自动变为 Running
- **关联需求**: FSD §14.7

### TC-BACKTEST-018: 回测 — 数据不足处理
- **前置条件**: 数据范围内 K 线数据不足 30 根
- **测试步骤**:
  1. 创建回测任务
- **预期结果**: 状态变为 Failed，错误信息"数据不足，至少需要 30 根 K 线"
- **关联需求**: FSD §14.2

### TC-BACKTEST-033: 回测 — 数据刚好 30 根（边界值）
- **前置条件**: 数据范围内刚好 30 根 K 线，策略条件树有效
- **测试步骤**:
  1. 创建回测任务
- **预期结果**: 回测正常执行，不触发数据不足错误（指标 warmup 不足的部分返回 null，策略评估容错处理）
- **关联需求**: FSD §14.3

### TC-BACKTEST-019: 回测 — 大数据量内存安全
- **前置条件**: 一年 1 分钟粒度数据（约 52 万条 K 线），复杂策略
- **测试步骤**:
  1. 创建并执行回测
- **预期结果**: 回测正常完成，无 OOM 或内存泄漏，内存峰值 < 1GB
- **关联需求**: TAD §9.1, FSD §14.7

### TC-BACKTEST-020: 回测结果导出
- **前置条件**: 存在已完成回测
- **测试步骤**:
  1. `GET /api/backtest/{id}/export` 指定格式 csv
- **预期结果**: 返回 CSV 文件下载，包含所有交易记录和时间序列 equity curve
- **关联需求**: FR-12.3, FSD §7.9.2

### TC-BACKTEST-021: K 线回放 — 已完成任务 SSE 回放
- **前置条件**: 已完成回测任务，AnalysisJson 包含 1000+ 根 K 线
- **测试步骤**:
  1. 打开任务详情 → 切换到 K 线分析标签
  2. 观察 K 线图逐根渲染过程
- **预期结果**: K 线以默认速度（300ms/根）逐根出现，进度从 0/N 递增到 N/N
- **关联需求**: FSD §14.5

### TC-BACKTEST-022: K 线回放 — 速度即时切换
- **前置条件**: K 线回放进行中
- **测试步骤**:
  1. 点击 4x 按钮
- **预期结果**: 回放速度即时变快（约 75ms/根），无需重建连接或重新加载数据
- **关联需求**: FSD §14.5.4

### TC-BACKTEST-023: K 线回放 — 暂停/继续/重放
- **前置条件**: K 线回放进行中
- **测试步骤**:
  1. 点击暂停按钮 → K 线图停止更新
  2. 点击继续按钮 → K 线从暂停处继续逐根渲染
  3. 点击重放按钮 → 回到第 1 根重新开始
- **预期结果**: 暂停/继续/重放功能正常
- **关联需求**: FSD §14.5.4

### TC-BACKTEST-024: K 线回放 — 运行中任务实时推送
- **前置条件**: 正在执行的回测任务
- **测试步骤**:
  1. 打开任务详情 → 切换到 K 线分析标签
  2. 回到回测任务列表页面
- **预期结果**: K 线图实时显示已完成的 K 线分析结果，不计入原始的 K 线根数随回测进度增加
- **关联需求**: FSD §14.5.1

### TC-BACKTEST-025: K 线分析 — 表格分页
- **前置条件**: 存在已完成回测，包含 1000+ 根 K 线
- **测试步骤**:
  1. 切换到表格模式
  2. 点击"加载更多"
- **预期结果**: 首次显示 100 根，每次加载更多追加 100 根
- **关联需求**: FSD §14.5.2

### TC-BACKTEST-026: 并发调度 — ResourceMonitor 双维度阈值计算
- **前置条件**: `ResourceMonitor` 注入 Mock `IResourceProvider`（预设内存和 CPU 返回值）
- **测试步骤**:
  1. 模拟内存 = 200MB（低于 512），CPU = 30%（低于 50%）
- **预期结果**: mem_cap=3, cpu_cap=3, AllowedConcurrency=3
  2. 模拟内存 = 200MB，CPU = 65%（CpuWarning < cpu < CpuCritical）
- **预期结果**: mem_cap=3, cpu_cap=2, AllowedConcurrency=min(3,2)=2
  3. 模拟内存 = 700MB（MemoryWarning < mem < MemoryCritical），CPU = 30%
- **预期结果**: mem_cap=2, cpu_cap=3, AllowedConcurrency=min(2,3)=2
  4. 模拟内存 = 1200MB，CPU = 85%（CpuCritical < cpu < CpuAbsolute）
- **预期结果**: mem_cap=1, cpu_cap=1, AllowedConcurrency=1
  5. 模拟内存 = 2000MB（MemoryAbsolute 超），CPU = 95%（CpuAbsolute 超）
- **预期结果**: mem_cap=0, cpu_cap=0, AllowedConcurrency=0
- **关联需求**: FSD §14.7.2

### TC-BACKTEST-027: 并发调度 — BacktestScheduler acquire/release 并发控制
- **前置条件**: 系统配置 MaxConcurrency=3，已提交 5 个回测任务
- **测试步骤**:
  1. 检查 DB 中状态
- **预期结果**: 3 个任务状态为 Running，2 个为 Pending
  2. 其中一个 Running 完成
- **预期结果**: 一个 Pending 自动变为 Running（所有 Running 完成后全部进入 Completed）
- **关联需求**: FSD §14.7.1

### TC-BACKTEST-028: 并发调度 — 多 Worker 队列消费
- **前置条件**: MaxConcurrency=2，同时入队 4 个任务
- **测试步骤**:
  1. 等待调度器稳定（Worker 完成初始化并开始消费）
- **预期结果**: 至少 2 个任务进入 Running，其余保持 Pending（证明 Worker 池动态消费队列，无需断言精确的入队顺序）
- **关联需求**: FSD §14.7.1

### TC-BACKTEST-029: 并发调度 — 内存水位上升后自动降级
- **前置条件**: 当前 AllowedConcurrency=3（内存 < 512MB），3 个 Worker 正在执行
- **测试步骤**:
  1. 模拟内存飙升到 1200MB
- **预期结果**: AllowedConcurrency 降为 1；3 个进行中的任务继续执行
  2. 其中一个任务完成后
- **预期结果**: 下一任务无法 acquire 槽位（allowed=1 < running=2），停留在等待循环
- **关联需求**: FSD §14.7.2

### TC-BACKTEST-030: 并发调度 — 内存水位下降后自动恢复
- **前置条件**: AllowedConcurrency=1，1 个 Worker 执行中，其余 Worker 在 200ms 轮询等待
- **测试步骤**:
  1. 模拟内存回落到 300MB
- **预期结果**: AllowedConcurrency 恢复为 3
  2. 当前 Worker 完成后
- **预期结果**: 等待中的 Worker 获取到槽位，开始执行新任务（至多 200ms 延迟）
- **关联需求**: FSD §14.7.4

### TC-BACKTEST-031: 并发调度 — Health 端点暴露调度指标
- **前置条件**: 服务运行中
- **测试步骤**:
  1. `GET /api/health`
- **预期结果**: 响应 body 包含 `backtestScheduler.runningCount`、`backtestScheduler.allowedConcurrency`、`backtestScheduler.currentMemoryMb`、`backtestScheduler.currentCpuPercent`
- **关联需求**: FSD §14.7.5

### TC-BACKTEST-032: 并发调度 — CPU 水位上升后自动降级
- **前置条件**: 当前内存低位（< MemoryWarningMb），CPU = 30%（低于 CpuWarningPercent=50%），3 个 Worker 正在执行
- **测试步骤**:
  1. 模拟 CPU 飙升到 65%（CpuWarning < cpu < CpuCritical），内存保持低位
- **预期结果**: mem_cap=3, cpu_cap=2, AllowedConcurrency=min(3,2)=2；3 个进行中的任务继续执行
  2. 其中一个任务完成后
- **预期结果**: 等待中的 Worker 因 AllowedConcurrency=2 无法 acquire 槽位（running=2），停留在等待循环
- **关联需求**: FSD §14.7.2

---

## M5 — 通知模块 (Notifications)

> 覆盖 AC-13 (TC-NOTIF-001 ~ 012)。对应 FR-09.x、FSD §7.11、FSD §13、TAD §3.3.5。

### TC-NOTIF-001: 创建通知渠道 — Telegram
- **前置条件**: 提供有效的 Telegram Bot Token 和 Chat ID
- **测试步骤**:
  1. `POST /api/notifications/channels` body: { type: "telegram", config: { botToken, chatId } }
- **预期结果**: 返回 201，渠道状态为 Active
  2. 发送测试消息
- **预期结果**: Telegram 消息送达
- **关联需求**: FR-09.1, FSD §7.11.1

### TC-NOTIF-002: 创建通知渠道 — Discord
- **前置条件**: 提供有效的 Discord Webhook URL
- **测试步骤**:
  1. `POST /api/notifications/channels` body: { type: "discord", config: { webhookUrl } }
- **预期结果**: 返回 201，渠道状态为 Active
  2. 发送测试消息
- **预期结果**: Discord 消息送达
- **关联需求**: FR-09.1, FSD §7.11.1

### TC-NOTIF-003: 创建通知渠道 — Email (SMTP)
- **前置条件**: 提供有效的 SMTP 配置
- **测试步骤**:
  1. `POST /api/notifications/channels` body: { type: "email", config: { smtpHost, smtpPort, username, password, recipients } }
- **预期结果**: 返回 201，渠道状态为 Active
  2. 发送测试消息
- **预期结果**: 邮件送达收件箱（非垃圾箱）
- **关联需求**: FR-09.1, FSD §7.11.1

### TC-NOTIF-004: 创建通知渠道 — 无效配置
- **前置条件**: 提供无效的 Telegram Bot Token
- **测试步骤**:
  1. `POST /api/notifications/channels` body: { type: "telegram", config: { botToken: "invalid", chatId: "123" } }
- **预期结果**: 返回 400，错误信息"Telegram Bot Token 验证失败"
- **关联需求**: FSD §7.11.1

### TC-NOTIF-005: 通知渠道列表与分页
- **前置条件**: 存在 8 个通知渠道
- **测试步骤**:
  1. `GET /api/notifications/channels?page=1&pageSize=5`
- **预期结果**: 返回 5 条记录，totalCount=8，hasNext=true
- **关联需求**: FSD §7.11.2

### TC-NOTIF-006: 更新通知渠道
- **前置条件**: 存在 Telegram 渠道
- **测试步骤**:
  1. `PUT /api/notifications/channels/{id}` 更新 chatId
- **预期结果**: 返回 200，渠道配置已更新，测试消息发送到新 chatId
- **关联需求**: FSD §7.11.3

### TC-NOTIF-007: 删除通知渠道
- **前置条件**: 存在通知渠道
- **测试步骤**:
  1. `DELETE /api/notifications/channels/{id}`
- **预期结果**: 返回 204，再次查询返回 404
- **关联需求**: FSD §7.11.4

### TC-NOTIF-008: 测试消息发送
- **前置条件**: 存在 Active 渠道
- **测试步骤**:
  1. `POST /api/notifications/channels/{id}/test`
- **预期结果**: 返回 200，渠道收到测试消息
- **关联需求**: FSD §7.11.5

### TC-NOTIF-009: 交易通知 — 订单成交推送
- **前置条件**: 通知渠道已配置，绑定交易事件
- **测试步骤**:
  1. 一笔订单成交（Filled）
- **预期结果**: 绑定的渠道收到成交通知，含订单 ID、交易对、方向、数量、价格
- **关联需求**: FR-09.2, FSD §13

### TC-NOTIF-010: 通知 — 风控告警推送
- **前置条件**: 通知渠道已配置
- **测试步骤**:
  1. 触发风控告警（如日亏损超限）
- **预期结果**: 紧急通知实时送达所有渠道
- **关联需求**: FR-09.3, FSD §13

### TC-NOTIF-011: 通知失败重试
- **前置条件**: 通知渠道配置正确，但暂时不可达（如 Telegram API 超时）
- **测试步骤**:
  1. 发送通知
- **预期结果**: 自动重试 3 次，间隔递增（1s/5s/15s）
  2. 所有重试均失败
- **预期结果**: 渠道标记为 Degraded，日志记录失败原因
- **关联需求**: FSD §13

### TC-NOTIF-012: 通知频率限制
- **前置条件**: 配置每分钟最大通知数 = 30
- **测试步骤**:
  1. 1 秒内发送 50 条通知
- **预期结果**: 前 30 条送达，后 20 条被限流丢弃，日志记录"Rate limit exceeded"
- **关联需求**: FSD §13

---

## M5 — 审计日志 (Audit Log)

> 覆盖 AC-14 (TC-AUDIT-001 ~ 010)。对应 FR-10.x、FSD §7.12、FSD §22、TAD §8.2。

### TC-AUDIT-001: 审计日志自动记录 — 用户登录
- **前置条件**: 用户已注册
- **测试步骤**:
  1. 用户登录成功
- **预期结果**: 审计日志新增记录，action=UserLogin，含用户名、IP、时间戳、UserAgent
- **关联需求**: FR-10.1, FSD §22

### TC-AUDIT-002: 审计日志自动记录 — 交易操作
- **前置条件**: 用户已登录，有足够余额
- **测试步骤**:
  1. 下达买入订单
- **预期结果**: 审计日志记录 OrderCreated，含订单 ID、交易对、方向、数量、价格
- **关联需求**: FR-10.1, FSD §22

### TC-AUDIT-003: 审计日志自动记录 — 风控事件
- **前置条件**: 风控模块运行中
- **测试步骤**:
  1. 触发 Kill Switch
- **预期结果**: 审计日志记录 SystemEmergencyStop，含触发来源
- **关联需求**: FR-10.1, FSD §22

### TC-AUDIT-004: 审计日志自动记录 — 策略变更
- **前置条件**: 存在已启用策略
- **测试步骤**:
  1. 禁用策略
- **预期结果**: 审计日志记录 StrategyDisabled，含策略 ID、操作人
- **关联需求**: FR-10.1, FSD §22

### TC-AUDIT-005: 审计日志自动记录 — 系统配置变更
- **前置条件**: Admin 用户已登录
- **测试步骤**:
  1. 修改系统配置参数
- **预期结果**: 审计日志记录 SystemConfigChanged，含变更前后值对比
- **关联需求**: FR-10.1, FSD §22

### TC-AUDIT-006: 审计日志查询 — 按时间范围筛选
- **前置条件**: 存在 30 天内和 30 天前的审计记录
- **测试步骤**:
  1. `GET /api/audit-logs?startTime=2026-03-01&endTime=2026-03-31`
- **预期结果**: 仅返回指定时间范围内的记录
- **关联需求**: FR-10.2, FSD §7.12

### TC-AUDIT-007: 审计日志查询 — 按操作类型筛选
- **前置条件**: 存在多种操作类型的日志
- **测试步骤**:
  1. `GET /api/audit-logs?action=UserLogin`
- **预期结果**: 仅返回 UserLogin 类型的记录
- **关联需求**: FSD §7.12

### TC-AUDIT-008: 审计日志查询 — 组合筛选 + 分页
- **前置条件**: 存在 50 条审计记录
- **测试步骤**:
  1. `GET /api/audit-logs?startTime=...&endTime=...&action=OrderCreated&page=1&pageSize=10&sortBy=timestamp&sortDir=desc`
- **预期结果**: 返回按时间倒序排列的 10 条记录，totalCount 正确
- **关联需求**: FSD §7.12

### TC-AUDIT-009: 审计日志不可篡改
- **前置条件**: 存在审计记录
- **测试步骤**:
  1. 尝试直接修改数据库中的审计日志
- **预期结果**: 数据库约束禁止 UPDATE/DELETE 操作，仅允许 INSERT
- **关联需求**: FR-10.3, TAD §8.2

### TC-AUDIT-010: 审计日志保留策略 — 超期自动清理
- **前置条件**: 配置保留期 180 天，存在超过 180 天的审计记录
- **测试步骤**:
  1. 触发数据清理任务（每日定时或手动）
- **预期结果**: 超过 180 天的记录被删除，清理任务记录自身日志
- **关联需求**: FR-10.4, TAD §8.2

---

## M5 — 仪表盘 (Dashboard)

> 覆盖 Dashboard 聚合查询。对应 FR-11.x、FSD §7.10。

### TC-DASH-001: Dashboard 摘要 — 正常返回
- **前置条件**: 系统运行中，存在活跃策略、持仓和交易记录
- **测试步骤**:
  1. `GET /api/dashboard/summary`
- **预期结果**: 返回 200，body 包含 totalPnl, totalPositions, activeStrategies, dailyPnl, winRate, totalTrades
- **关联需求**: FR-11.1, FSD §7.10

### TC-DASH-002: Dashboard 摘要 — 无交易记录时返回零值
- **前置条件**: 新系统，无任何交易记录
- **测试步骤**:
  1. `GET /api/dashboard/summary`
- **预期结果**: 返回 200，所有数值字段为 0 或空列表
- **关联需求**: FSD §7.10

### TC-DASH-003: Dashboard 摘要 — 权限校验
- **前置条件**: Viewer 角色用户已登录
- **测试步骤**:
  1. `GET /api/dashboard/summary`
- **预期结果**: 返回 200（Viewer 可读）
- **关联需求**: FSD §7.10, FSD §19

### TC-DASH-004: Dashboard 摘要 — 数据一致性
- **前置条件**: 有 3 个策略，总 PnL = 各策略 PnL 之和
- **测试步骤**:
  1. `GET /api/dashboard/summary`
  2. 手动计算各策略 PnL 之和
- **预期结果**: dashboard.totalPnl == 手动计算结果
- **关联需求**: FSD §7.10

### TC-DASH-005: Dashboard 摘要 — 响应时间
- **前置条件**: 100 个策略，5000 条交易记录
- **测试步骤**:
  1. `GET /api/dashboard/summary`
  2. 记录响应时间
- **预期结果**: P95 响应时间 < 500ms
- **关联需求**: TAD §9.1

### TC-DASH-006: Dashboard 数据隔离 — Trader 仅看自己数据
- **前置条件**: Trader A（有策略 S1、S2）和 Trader B（有策略 S3）同时在线
- **测试步骤**:
  1. Trader A 请求 `GET /api/dashboard/summary`
  2. Trader B 请求 `GET /api/dashboard/summary`
- **预期结果**: Trader A 的摘要仅包含 S1+S2 数据，Trader B 的摘要仅包含 S3 数据，两者互不干扰
- **关联需求**: FR-11.1, FSD §7.10, FSD §19

---

## M5 — 崩溃恢复与 Reconciliation (Crash Recovery)

> 覆盖 AC-11 (TC-RECOV-001 ~ 008)。对应 FR-14.x、FSD §16、TAD §9.2。

### TC-RECOV-001: 重启后 Startup Sync — 加载策略
- **前置条件**: 系统崩溃前有 5 个已启用策略
- **测试步骤**:
  1. 重启系统
- **预期结果**: 启动后所有 5 个策略仍为启用状态，Trading Engine 加载策略列表
- **关联需求**: FR-14.1, FSD §16.1

### TC-RECOV-002: 重启后 Startup Sync — 恢复交易所连接
- **前置条件**: 系统崩溃前已配置 3 个交易所
- **测试步骤**:
  1. 重启系统
- **预期结果**: 所有 3 个交易所自动重连，连接状态变为 Connected
- **关联需求**: FSD §16.1

### TC-RECOV-003: 订单 Reconciliation — 本地挂单与交易所同步
- **前置条件**: 系统崩溃前有 5 笔本地标记为 Open 的挂单
- **测试步骤**:
  1. 重启后执行 Reconciliation
- **预期结果**: 查询交易所状态，匹配本地订单：已成交的标记为 Filled，已取消的标记为 Canceled，仍在的保留 Open
- **关联需求**: FR-14.2, FSD §16.2

### TC-RECOV-004: 订单 Reconciliation — 交易所存在本地未知订单
- **前置条件**: 系统崩溃期间，交易所上有通过其他终端创建的挂单
- **测试步骤**:
  1. 重启后执行 Reconciliation
- **预期结果**: 检测到孤儿订单，记录告警日志，可选自动撤销
- **关联需求**: FSD §16.2

### TC-RECOV-005: 订单 Reconciliation — PnL 重新计算
- **前置条件**: 崩溃期间有订单成交
- **测试步骤**:
  1. Reconciliation 完成后
- **预期结果**: 本地 PnL 与交易所历史成交记录计算一致
- **关联需求**: FSD §16.2

### TC-RECOV-006: 数据完整性 — 未持久化的信号在崩溃后丢失
- **前置条件**: Trading Engine 处理信号时系统崩溃
- **测试步骤**:
  1. 检查重启后的状态
- **预期结果**: 已持久化的订单不受影响，未持久化的信号根据配置重放或丢弃
- **关联需求**: FR-14.3, FSD §16.3

### TC-RECOV-007: 恢复时间目标 (RTO) — 全量恢复 < 30s
- **前置条件**: 5 个策略，3 个交易所，50 笔挂单
- **测试步骤**:
  1. 停止系统
  2. 启动系统并计时至 Trading Engine 恢复运行
- **预期结果**: 恢复时间 < 30s
- **关联需求**: TAD §9.2

### TC-RECOV-008: 恢复点目标 (RPO) — 零数据丢失
- **前置条件**: 系统处理订单过程中崩溃
- **测试步骤**:
  1. 检查重启后数据
- **预期结果**: 已确认的订单全部恢复，无丢失（RPO = 0）
- **关联需求**: TAD §9.2

---

## M6 — Docker 部署 (Deployment)

> 覆盖 AC-01 (TC-DEPLOY-001 ~ 006)、AC-17 (TC-DEPLOY-010)。对应 FSD §23、PRD §16.x。

### TC-DEPLOY-001: Docker Compose 全服务启动
- **前置条件**: Docker Engine 运行，docker-compose.yml 就绪
- **测试步骤**:
  1. `docker compose up -d`
- **预期结果**: 所有服务启动成功（tradex 等），状态为 healthy
- **关联需求**: FR-16.1, AC-01, FSD §23

### TC-DEPLOY-002: 容器健康检查
- **前置条件**: 服务已启动
- **测试步骤**:
  1. `docker compose ps`
- **预期结果**: 每个服务 health status 为 healthy
- **关联需求**: FSD §23

### TC-DEPLOY-003: 端口映射正确
- **前置条件**: Docker Compose 已启动
- **测试步骤**:
  1. 检查 `curl http://localhost:5000/api/health`
- **预期结果**: 返回 200，API 可访问
- **关联需求**: FSD §23

### TC-DEPLOY-004: 持久化数据卷
- **前置条件**: Docker Compose 已启动，数据库中有数据
- **测试步骤**:
  1. `docker compose down`
  2. `docker compose up -d`
  3. 查询数据库
- **预期结果**: 数据持久化，重启后数据不丢失
- **关联需求**: FR-16.2, FSD §23

### TC-DEPLOY-005: 服务依赖顺序 — 数据库先于 API
- **前置条件**: Docker Compose 配置了 depends_on
- **测试步骤**:
  1. 检查启动日志
- **预期结果**: tradex-db 健康后再启动 tradex-api（depends_on condition: service_healthy）
- **关联需求**: FSD §23

### TC-DEPLOY-006: 日志轮转配置
- **前置条件**: Docker Compose 已配置 log rotation
- **测试步骤**:
  1. 检查容器日志驱动配置
- **预期结果**: `max-size: 10m`, `max-file: 3` 生效，日志文件不无限增长
- **关联需求**: FSD §23

### TC-DEPLOY-007: 多环境 Docker Compose 覆盖
- **前置条件**: 存在 docker-compose.override.yml 或 .env 文件
- **测试步骤**:
  1. 设置环境变量 `ASPNETCORE_ENVIRONMENT=Staging`
  2. `docker compose up -d`
- **预期结果**: 容器加载对应的 staging 配置
- **关联需求**: FSD §23

### TC-DEPLOY-008: 容器资源限制
- **前置条件**: docker-compose.yml 配置了资源限制
- **测试步骤**:
  1. `docker compose inspect tradex-api`
- **预期结果**: 确认 mem_limit、cpus 等限制生效
- **关联需求**: FSD §23

### TC-DEPLOY-009: 优雅关闭
- **前置条件**: 服务运行中，有活跃 WebSocket 连接
- **测试步骤**:
  1. `docker compose stop tradex-api`（发送 SIGTERM）
- **预期结果**: API 服务在 30s 内完成当前请求处理，关闭 WebSocket 连接，然后退出
- **关联需求**: FSD §23

### TC-DEPLOY-010: Swagger UI 可访问
- **前置条件**: API 服务运行中
- **测试步骤**:
  1. `GET /swagger` 或 `GET /swagger/v1/swagger.json`
- **预期结果**: Swagger UI 正常渲染，swagger.json 包含所有 API 端点定义
- **关联需求**: AC-17, FSD §7.1

---

## M6 — 安全 (Security)

> 覆盖 AC-10-C (TC-SEC-010 ~ 015) 及补充安全测试。对应 FSD §20、TAD §9.3。

### TC-SEC-001: SQL 注入防护
- **前置条件**: 用户已登录
- **测试步骤**:
  1. 在 API 参数中注入 SQL（如 `?id=1;DROP TABLE Users--`）
- **预期结果**: 返回 400 或 500，不执行注入 SQL，数据库不受影响
- **关联需求**: FSD §20.1

### TC-SEC-002: XSS 防护
- **前置条件**: 用户已登录
- **测试步骤**:
  1. `POST /api/strategies` name 字段包含 `<script>alert('xss')</script>`
- **预期结果**: 输入被 HTML 编码或拒绝，页面不执行脚本
- **关联需求**: FSD §20.1

### TC-SEC-003: CSRF 防护
- **前置条件**: 用户已登录，存在 CSRF Token 机制
- **测试步骤**:
  1. 发送 POST 请求不带 CSRF Token
- **预期结果**: 返回 403 Forbidden
  2. 发送 POST 请求带有效 CSRF Token
- **预期结果**: 请求成功
- **关联需求**: FSD §20.1

### TC-SEC-004: 密码强度策略
- **前置条件**: 无
- **测试步骤**:
  1. `POST /api/auth/register` password = "123"
- **预期结果**: 返回 400，错误信息"密码长度至少 8 位，需包含大小写字母和数字"
  2. password = "StrongPass1"
- **预期结果**: 注册成功
- **关联需求**: FSD §20.2

### TC-SEC-005: 登录失败锁定
- **前置条件**: 用户已注册
- **测试步骤**:
  1. 连续 5 次登录密码错误
- **预期结果**: 第 5 次失败后账户锁定，返回 "Account locked due to too many failed attempts"
  2. 输入正确密码
- **预期结果**: 仍然被拒
  3. 等待锁定时间（默认 15 分钟）
- **预期结果**: 账户解锁，可正常登录
- **关联需求**: FSD §20.2

### TC-SEC-006: JWT Token 有效期
- **前置条件**: 用户已登录
- **测试步骤**:
  1. 获取 Access Token
  2. 等待 Token 过期（配置有效期后）
  3. 使用过期 Token 请求 API
- **预期结果**: 返回 401 Unauthorized，message="Token expired"
- **关联需求**: FSD §20.3

### TC-SEC-007: Refresh Token 轮换
- **前置条件**: 用户已登录
- **测试步骤**:
  1. `POST /api/auth/refresh` 使用有效 Refresh Token
- **预期结果**: 返回新的 Access Token + 新的 Refresh Token
  2. 再次使用旧的 Refresh Token
- **预期结果**: 返回 401，旧的 Refresh Token 已失效
- **关联需求**: FSD §20.3

### TC-SEC-008: HTTPS 强制跳转
- **前置条件**: 生产环境配置
- **测试步骤**:
  1. `curl http://api.tradex.com`（HTTP）
- **预期结果**: 返回 301/308 跳转到 `https://api.tradex.com`
- **关联需求**: FSD §20.1

### TC-SEC-009: 敏感信息脱敏 — API Key 不可明文返回
- **前置条件**: 已配置交易所 API Key
- **测试步骤**:
  1. `GET /api/exchanges/{id}`
- **预期结果**: 返回的 apiKey 被掩码处理（如 `abc*****xyz`），apiSecret 不返回
- **关联需求**: FSD §20.4

### TC-SEC-010: IP 白名单 — 允许的 IP 可访问
- **前置条件**: IP 白名单配置包含 192.168.1.0/24
- **测试步骤**:
  1. 从 192.168.1.100 发送 API 请求
- **预期结果**: 请求正常处理，返回 200
- **关联需求**: AC-10-C, FSD §20.5

### TC-SEC-011: IP 白名单 — 非白名单 IP 被拒绝
- **前置条件**: IP 白名单配置仅允许 192.168.1.0/24
- **测试步骤**:
  1. 从 10.0.0.1 发送 API 请求
- **预期结果**: 返回 403 Forbidden，日志记录"IP not in whitelist"
- **关联需求**: AC-10-C, FSD §20.5

### TC-SEC-012: IP 白名单 — 动态更新
- **前置条件**: IP 白名单当前仅包含 192.168.1.0/24
- **测试步骤**:
  1. Admin 添加 10.0.0.0/8 到白名单
  2. 从 10.0.0.1 发送 API 请求
- **预期结果**: 请求正常处理，新配置立即生效
- **关联需求**: AC-10-C, FSD §20.5

### TC-SEC-013: API 密钥认证
- **前置条件**: 生成 API Key + Secret 用于程序化访问
- **测试步骤**:
  1. `GET /api/v1/positions` 使用有效 API Key 签名
- **预期结果**: 返回 200
  2. 使用无效 API Key
- **预期结果**: 返回 401
- **关联需求**: FSD §20.3

### TC-SEC-014: Casbin RBAC — 角色权限验证
- **前置条件**: Admin、Trader、Viewer 三个角色各有不同权限
- **测试步骤**:
  1. Viewer `DELETE /api/strategies/{id}`
- **预期结果**: 返回 403 Forbidden
  2. Admin `DELETE /api/strategies/{id}`
- **预期结果**: 返回 204
- **关联需求**: FSD §19

### TC-SEC-015: Casbin RBAC — 拒绝所有未显式允许的操作
- **前置条件**: Casbin 策略已加载
- **测试步骤**:
  1. 尝试未在 policy.csv 中定义的操作
- **预期结果**: Casbin 默认拒绝，返回 403
- **关联需求**: FSD §19

---

## M7 — 性能 (Performance)

> 覆盖性能基准测试。对应 TAD §9.1。

### TC-PERF-001: Trading Engine 周期 — 15s 内完成一轮
- **前置条件**: 50 个活跃策略，3 个交易所连接
- **测试步骤**:
  1. 启动系统，记录连续 100 个 Trading Engine 执行周期的耗时
- **预期结果**: P95 周期耗时 < 15s，P99 < 20s
- **关联需求**: TAD §9.1

### TC-PERF-002: REST API 响应时间 — P95 < 500ms
- **前置条件**: 系统正常负载
- **测试步骤**:
  1. 使用压测工具（如 k6）并发 50 用户，持续 5 分钟
  2. 记录各 API 端点 P95 响应时间
- **预期结果**: 所有 GET 端点 P95 < 500ms，POST/PUT/DELETE P95 < 1000ms
- **关联需求**: TAD §9.1

### TC-PERF-003: 数据管道吞吐量
- **前置条件**: 行情数据源推送 1 秒 1 条 K 线更新
- **测试步骤**:
  1. 监控 DataPipeline 处理延迟
- **预期结果**: 端到端延迟（数据到达 → 策略消费）< 500ms
- **关联需求**: TAD §9.1

### TC-PERF-004: 并发用户支持
- **前置条件**: API 服务运行中
- **测试步骤**:
  1. k6 模拟 200 并发用户，混合读写请求，持续 10 分钟
- **预期结果**: 错误率 < 1%，P95 响应时间符合目标
- **关联需求**: TAD §9.1

### TC-PERF-005: 内存稳定性 — 无泄漏
- **前置条件**: 系统运行中，持续 24 小时
- **测试步骤**:
  1. 每小时记录一次内存使用量
- **预期结果**: 内存使用趋于稳定，无持续增长趋势（无内存泄漏）
- **关联需求**: TAD §9.1

---

## M7 — 构建流水线 (Build)

> 覆盖 AC-18 (TC-BUILD-001)。对应构建规范。

### TC-BUILD-001: 完整构建流水线
- **前置条件**: 代码已拉取到最新，Docker 可用
- **测试步骤**:
  1. `dotnet build`
- **预期结果**: 编译成功，无错误无警告
  2. `dotnet test`
- **预期结果**: 全部测试通过，覆盖率 ≥ 80%
  3. `docker compose build`
- **预期结果**: Docker 镜像构建成功
- **关联需求**: AC-18

### TC-BUILD-002: 代码分析 — 无弃用 API 使用
- **前置条件**: 项目配置了 Roslyn 分析器
- **测试步骤**:
  1. `dotnet build` 启用分析器
- **预期结果**: 无 `SYSLIB` 弃用警告，无严重代码分析错误
- **关联需求**: AGENTS.md

### TC-BUILD-003: NuGet 包漏洞扫描
- **前置条件**: 配置了包审计
- **测试步骤**:
  1. `dotnet list package --vulnerable`
- **预期结果**: 无已知漏洞依赖
- **关联需求**: AGENTS.md

---

## M7 — WebSocket / SignalR (Real-time Push)

> 覆盖实时推送功能。对应 FSD §17、TAD §3.2.5。

### TC-WS-001: SignalR 连接建立
- **前置条件**: 用户已登录，获取有效 Token
- **测试步骤**:
  1. 客户端 `new HubConnectionBuilder().withUrl("/hubs/trading")` 建立连接
- **预期结果**: 连接成功，`OnConnectedAsync` 触发
- **关联需求**: FSD §17.1

### TC-WS-002: SignalR 连接 — 无效 Token 拒绝
- **前置条件**: 使用无效/过期 Token
- **测试步骤**:
  1. 客户端尝试建立连接
- **预期结果**: 返回 401，连接被拒绝
- **关联需求**: FSD §17.1

### TC-WS-003: 实时行情推送
- **前置条件**: SignalR 连接已建立，订阅了行情频道
- **测试步骤**:
  1. 模拟新的 K 线数据到达
- **预期结果**: 客户端收到 `OnPriceUpdate` 回调，含交易对、最新价、时间戳
- **关联需求**: FR-04.1, FSD §17.2

### TC-WS-004: 订单状态实时推送
- **前置条件**: SignalR 连接已建立，订阅了订单频道
- **测试步骤**:
  1. 一笔订单状态变更（Pending → Filled）
- **预期结果**: 客户端收到 `OnOrderUpdate` 回调，含订单 ID、新状态、成交明细
- **关联需求**: FR-07.5, FSD §17.2

### TC-WS-005: 风控事件实时推送
- **前置条件**: SignalR 连接已建立
- **测试步骤**:
  1. 触发风控拦截事件
- **预期结果**: 客户端收到 `OnRiskAlert` 回调，含拦截原因、策略 ID
- **关联需求**: FSD §17.2

### TC-WS-006: 连接断开自动重连
- **前置条件**: SignalR 连接已建立
- **测试步骤**:
  1. 服务器重启或网络中断
- **预期结果**: 客户端自动重连（最多 5 次，间隔递增），重连后恢复订阅
- **关联需求**: FSD §17.3

### TC-WS-007: 组播隔离 — 用户只收到自己相关的通知
- **前置条件**: 两个不同用户 A、B 各自建立连接
- **测试步骤**:
  1. 用户 A 触发订单更新
- **预期结果**: 仅用户 A 收到推送，用户 B 不受影响
- **关联需求**: FSD §17.1

### TC-WS-008: SignalR Hub 并发连接压力
- **前置条件**: 服务器配置了最大连接数
- **测试步骤**:
  1. 500 个客户端并发连接
- **预期结果**: 连接全部成功，服务器资源使用正常，无崩溃
- **关联需求**: TAD §9.1

---

## M7 — 移动止损/止盈 (Trailing Stop / Take Profit)

> 覆盖高级订单类型。对应 FR-07.3、FSD §7.7。

### TC-TRAIL-001: 移动止损 — 价格上涨时止损价跟随
- **前置条件**: 持有多头仓位，设置移动止损（激活价 100，回调 5%）
- **测试步骤**:
  1. 价格从 100 上涨到 120
- **预期结果**: 止损价从 95 跟随上涨到 114（120 * 0.95）
  2. 价格从 120 回调到 113（回调超过 5%）
- **预期结果**: 触发止损，市价平仓
- **关联需求**: FR-07.3, FSD §7.7

### TC-TRAIL-002: 移动止损 — 价格未达激活价不触发
- **前置条件**: 持有多头仓位，激活价 100
- **测试步骤**:
  1. 价格在 95 ~ 99 之间波动
- **预期结果**: 移动止损未激活，无止损平仓
- **关联需求**: FSD §7.7

### TC-TRAIL-003: 移动止损 — 价格未回调到阈值不止损
- **前置条件**: 多头仓位，移动止损已激活，当前止损价 114
- **测试步骤**:
  1. 价格从 120 回调到 116（回调 3.3% < 5%）
- **预期结果**: 不止损，止损价保持 114
- **关联需求**: FSD §7.7

### TC-TRAIL-004: 止盈限价单
- **前置条件**: 持有多头仓位，设置止盈价 150
- **测试步骤**:
  1. 价格达到 150
- **预期结果**: 系统提交限价卖单，价格 150
- **关联需求**: FR-07.3, FSD §7.7

### TC-TRAIL-005: 止损/止盈同时设置
- **前置条件**: 持有多头仓位，止损 90，止盈 150
- **测试步骤**:
  1. 价格跌到 90
- **预期结果**: 触发止损，市价平仓
  2. 价格涨到 150
- **预期结果**: 触发止盈，限价平仓（测试步 1 和 2 分别测试，互斥）
- **关联需求**: FSD §7.7

### TC-TRAIL-006: 移动止损修改
- **前置条件**: 持仓已设置移动止损
- **测试步骤**:
  1. `PUT /api/orders/trailing-stop/{positionId}` body: { activationPrice: 110, callbackPercent: 3 }
- **预期结果**: 返回 200，移动止损参数已更新，后续按新参数计算
- **关联需求**: FSD §7.7

### TC-TRAIL-007: 移动止损取消
- **前置条件**: 持仓已设置移动止损
- **测试步骤**:
  1. `DELETE /api/orders/trailing-stop/{positionId}`
- **预期结果**: 返回 204，移动止损已移除，不再自动平仓
- **关联需求**: FSD §7.7

### TC-TRAIL-008: 部分平仓后移动止损调整
- **前置条件**: 持仓 10 BTC，设置了移动止损
- **测试步骤**:
  1. 手动平仓 5 BTC
- **预期结果**: 移动止损的触发数量自动调整为 5 BTC
- **关联需求**: FSD §7.7

---

## M7 — 错误处理与中间件 (Error Handling)

> 覆盖全局异常中间件、请求验证、CORS、幂等性。对应 TAD §8.1、FSD §20。

### TC-ERR-001: 全局异常中间件 — 未授权访问返回 401
- **前置条件**: 未登录或未提供 Token
- **测试步骤**:
  1. `GET /api/strategies` 不带 Authorization 头
- **预期结果**: 返回 401，body 为标准化错误响应 `{ "code": "Unauthorized", "message": "...", "traceId": "..." }`
- **关联需求**: TAD §8.1

### TC-ERR-002: 全局异常中间件 — 404 统一响应
- **前置条件**: 无
- **测试步骤**:
  1. `GET /api/nonexistent-route`
- **预期结果**: 返回 404，body 为标准化错误响应 `{ "code": "NotFound", "message": "Route /api/nonexistent-route not found" }`
- **关联需求**: TAD §8.1

### TC-ERR-003: 全局异常中间件 — 服务器错误返回 500
- **前置条件**: 服务运行中
- **测试步骤**:
  1. 触发未处理的服务器异常（如模拟数据库连接失败）
- **预期结果**: 返回 500，body 包含 traceId，不暴露内部错误详情
- **关联需求**: TAD §8.1

### TC-ERR-004: 请求验证 — 模型校验失败返回 400
- **前置条件**: 无
- **测试步骤**:
  1. `POST /api/strategies` body 缺少必填字段 `name`
- **预期结果**: 返回 400，body 包含验证错误详情（字段名、错误原因）
- **关联需求**: TAD §8.1

### TC-ERR-005: 请求验证 — 类型错误返回 400
- **前置条件**: 无
- **测试步骤**:
  1. `POST /api/strategies` body 中 `maxPositionSize` 传入字符串 `"abc"`
- **预期结果**: 返回 400，body 指明类型不匹配
- **关联需求**: TAD §8.1

### TC-ERR-006: CORS — 允许的来源
- **前置条件**: CORS 配置允许 `https://app.tradex.com`
- **测试步骤**:
  1. `OPTIONS /api/strategies` Origin: `https://app.tradex.com`
- **预期结果**: 返回 204，`Access-Control-Allow-Origin: https://app.tradex.com`
- **关联需求**: FSD §20.1

### TC-ERR-007: CORS — 拒绝未允许的来源
- **前置条件**: CORS 配置允许 `https://app.tradex.com`
- **测试步骤**:
  1. `OPTIONS /api/strategies` Origin: `https://evil.com`
- **预期结果**: 返回 403，无 `Access-Control-Allow-Origin` 头
- **关联需求**: FSD §20.1

### TC-ERR-008: 幂等性 — 相同 Idempotency-Key 仅执行一次
- **前置条件**: 幂等性中间件配置
- **测试步骤**:
  1. `POST /api/orders` 带 `Idempotency-Key: abc-123` 创建订单
- **预期结果**: 返回 201，订单已创建
  2. 同样的请求再次发送
- **预期结果**: 返回 200（或 409），不创建重复订单
- **关联需求**: FSD §7.7

### TC-ERR-009: 健康检查端点
- **前置条件**: 服务运行中
- **测试步骤**:
  1. `GET /api/health`
- **预期结果**: 返回 200，body 包含 `{ "status": "Healthy", "uptime": "...", "database": "Healthy" }`
- **关联需求**: FSD §23

### TC-ERR-010: 健康检查 — 数据库断开时降级
- **前置条件**: 数据库服务停止
- **测试步骤**:
  1. `GET /api/health`
- **预期结果**: 返回 503，`{ "status": "Unhealthy", "database": "Unhealthy" }`
- **关联需求**: FSD §23
