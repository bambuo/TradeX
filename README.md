# TradeX

TradeX 是一个多交易所现货自动交易系统，基于 ASP.NET Core 10 + Vue 3。前端 SPA 内嵌至 ASP.NET Core 同一进程，单容器部署。

## 架构概览

```
        ┌────────── tradex 容器 :80 ──────────┐
        │  ASP.NET Core 10                     │
        │  ├── /api/*      → REST Controllers  │
        │  ├── /hubs/*     → SignalR           │
        │  ├── /health     → Health Check      │
        │  └── /*          → Vue SPA 静态文件    │
        └──────────────────────────────────────┘
                     │
        ┌────────────┴────────────┐
        ▼                         ▼
    SQLite (配置)            IoTDB (时序)
```

## 技术栈

| 层 | 技术 |
|---|------|
| 后端 | ASP.NET Core 10 + C# 14 |
| 前端 | Vue 3 + TypeScript + Pinia |
| 数据库 | SQLite + IoTDB |
| 鉴权 | Casbin.NET RBAC + JWT + MFA TOTP |
| 实时通信 | SignalR |
| 通知 | Telegram / Discord / Email |
| 部署 | Docker Compose |

## 快速启动

```bash
git clone <repo>
cd TradeX

# 设置 JWT Secret 并启动
JWT_SECRET=your-secret-key docker compose up --build
```

访问 `http://localhost` 即可打开 TradeX 管理界面。

### 初始化流程（首次启动）

1. 首次访问自动跳转到初始化向导页面
2. 设置系统参数、创建 Super Admin 账户、绑定 MFA
3. 完成后恢复正常模式，使用 Super Admin 登录

## 项目结构

```
TradeX/
├── Dockerfile               # 统一三阶段构建
├── docker-compose.yml       # tradex + iotdb 编排
├── backend/
│   ├── TradeX.slnx
│   ├── TradeX.Api/          # ASP.NET Core Web API + SPA 静态文件
│   ├── TradeX.Core/         # 领域模型、接口、枚举
│   ├── TradeX.Exchange/     # 交易所客户端 (Binance/OKX/Gate/Bybit/HTX)
│   ├── TradeX.Indicators/   # 技术指标 (Skender.Stock.Indicators)
│   ├── TradeX.Trading/      # 策略引擎 + 风控 + 回测 + Reconciliation
│   ├── TradeX.Infrastructure/ # EF Core + Casbin + IoTDB Client
│   ├── TradeX.Notifications/  # Telegram/Discord/Email 通知
│   └── TradeX.Tests/        # xUnit + NSubstitute (185 tests)
├── frontend/                # Vue 3 + TypeScript SPA
└── docs/                    # PRD, FSD, TAD, TestCases
```

## 开发

```bash
# 后端
cd backend && dotnet run --project TradeX.Api

# 前端（开发模式，代理 API 到后端）
cd frontend && npm run dev

# 测试
cd backend && dotnet test
```

## Docker 部署

```bash
# 构建并启动
JWT_SECRET=your-secret-key docker compose up --build

# 后台运行
JWT_SECRET=your-secret-key docker compose up --build -d

# 查看日志
docker compose logs -f
```

支持的交易所：Binance、OKX、Gate.io、Bybit、HTX。
