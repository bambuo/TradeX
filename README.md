# TradeX

TradeX 是一个多交易所现货自动交易系统，基于 ASP.NET Core 10 + Vue 3。

当前仓库已完成第一阶段脚手架（M1 起步）：
- 后端分层项目结构（Api/Core/Exchange/Indicators/Trading/Infrastructure/Notifications/Tests）
- 初始化模式（未初始化时仅开放 `/health` 与 `/api/setup/*`）
- SQLite 持久化基础模型（User、SystemConfig）
- 初始化 API（`GET /api/setup/status`、`POST /api/setup/initialize`）
- 登录 API 最小闭环（用户名密码校验 + MFA 流程占位返回）
- 前端 Vue3 + TS + Pinia 骨架（初始化向导 + 仪表盘占位）
- Dockerfile 与 docker-compose 对齐

## 快速启动

```bash
cd backend
dotnet build TradeX.slnx

cd ../
docker compose up --build
```

## 下一步建议（按 PRD）

1. 补齐鉴权：JWT + RefreshToken + TOTP + Recovery Codes
2. 引入 Casbin.NET 并实现 API 级 RBAC
3. 交易所账户 CRUD + API Key AES 存储 + 测试连接
4. SignalR 实时推送与交易引擎 BackgroundService
