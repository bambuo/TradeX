# TradeX 运维剧本

> 从零部署到故障演练的可执行命令集。所有命令假设工作目录在仓库根。

## 0. Pre-flight 检查清单

部署前**逐项**确认：

```bash
# 必填的环境变量
cat > .env <<EOF
MYSQL_ROOT_PASSWORD=$(openssl rand -base64 32)
MYSQL_PASSWORD=$(openssl rand -base64 32)
JWT_SECRET=$(openssl rand -base64 64)
EOF
chmod 600 .env

# 检查端口冲突（80 / 3306 / 6379 / 9464 / 9090 / 3000）
for port in 80 3306 6379 9464 9090 3000; do
  lsof -i :$port -sTCP:LISTEN && echo "⚠️ port $port busy" || echo "✓ port $port free"
done

# 检查磁盘可用空间（生产建议 ≥50GB）
df -h .
```

## 1. 初次部署

```bash
# 拉镜像 + 编译
docker compose --env-file .env build

# 启动栈（mysql 健康检查就绪后再起 api/worker）
docker compose --env-file .env up -d
docker compose ps  # 全部 healthy 才继续

# 应用 EF 迁移（首次必跑，之后随 PR）
docker compose exec backend dotnet TradeX.Api.dll migrate \
  || docker run --rm --network host \
       -e ConnectionStrings__DefaultConnection="Server=localhost;Port=3306;Database=tradex;User Id=tradex;Password=${MYSQL_PASSWORD}" \
       mcr.microsoft.com/dotnet/sdk:10.0 \
       sh -c "cd /src && dotnet ef database update --project TradeX.Infrastructure --startup-project TradeX.Api"

# 启动观测栈（可选）
docker compose -f docker-compose.yml -f docker-compose.observability.yml --env-file .env up -d prometheus grafana
```

## 2. Bootstrap 第一个 Admin

**注意**：所有敏感端点 (`[RequireMfa]`) 要求请求头 `X-MFA-Code`，但首次部署谁都没启用 MFA。Bootstrap 必须按以下顺序：

```bash
API=http://localhost

# 1) 创建首位 admin（需要 SetupGuardMiddleware 未关闭；初装流程见 Controllers/AuthController）
curl -sX POST $API/api/auth/register \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","email":"admin@example.com","password":"<strong>"}'

# 2) 登录获 JWT
TOKEN=$(curl -sX POST $API/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"<strong>"}' | jq -r .accessToken)

# 3) 启动 MFA：拿 QR 码 secret，扫码后提交 6 位码激活
SECRET=$(curl -s $API/api/auth/mfa/setup -H "Authorization: Bearer $TOKEN" | jq -r .secret)
echo "用 Authenticator 扫码: $SECRET"
read -p "输入当前 TOTP 6 位码: " CODE
curl -sX POST $API/api/auth/mfa/enable \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d "{\"code\":\"$CODE\"}"

echo "✓ Bootstrap 完成。从现在起敏感操作需带 X-MFA-Code 头。"
```

## 3. 健康烟雾测试（每次部署后跑）

```bash
# === API 健康 ===
curl -fs $API/health || { echo "✗ /health 失败"; exit 1; }
curl -fs $API/metrics | grep -c "tradex_" # 期望 ≥4 个自定义指标

# === Worker 心跳 ===
docker compose logs --tail 50 worker | grep -E "TradingEngine|BacktestScheduler|OrderReconcilerService" \
  && echo "✓ Worker 服务已装载"

# === Redis 三通道订阅 ===
docker compose exec redis redis-cli PUBSUB CHANNELS 'tradex:*'
# 期望看到: tradex:events, tradex:cmd, tradex:backtest, tradex.signalr.*

# === DB 连通 ===
docker compose exec mysql mysql -uroot -p${MYSQL_ROOT_PASSWORD} -e "USE tradex; SHOW TABLES;"

# === 手动触发对账（验证命令通道）===
curl -fsX POST $API/api/admin/reconcile-now \
  -H "Authorization: Bearer $TOKEN" \
  -H "X-MFA-Code: $CODE"
docker compose logs --tail 20 worker | grep -i "ReconcileNow 命令" && echo "✓ 命令通道工作"
```

## 4. 交易所实盘小金额验证

**目标**：每家交易所跑通"下单 → 反查 → 撤单"完整链路。建议金额 ≤$5。

按下面顺序，对每家交易所跑：**Binance / Bybit / OKX / Gate / HTX**

```bash
# 0) 录入交易所凭证（前端 UI 或 API）
curl -sX POST $API/api/exchanges \
  -H "Authorization: Bearer $TOKEN" -H "X-MFA-Code: $(authenticator)" \
  -H 'Content-Type: application/json' \
  -d '{
    "name":"Binance Test",
    "type":"Binance",
    "apiKey":"<read-only-key-first>",  # 先用只读 key 验证连接
    "secretKey":"<secret>",
    "isTestnet":true  # Binance/Bybit 有 testnet，强烈建议先用
  }'

# 1) TestConnection
EX_ID=$(curl -s $API/api/exchanges -H "Authorization: Bearer $TOKEN" | jq -r '.[0].id')
curl -sX POST $API/api/exchanges/$EX_ID/test \
  -H "Authorization: Bearer $TOKEN"
# 期望 200 + permissions 返回

# 2) 切换为交易 key（含 trade 权限）后重复 test

# 3) 手动下一笔最小金额订单
TRADER_ID=$(curl -s $API/api/traders -H "Authorization: Bearer $TOKEN" | jq -r '.[0].id')
ORDER=$(curl -sX POST $API/api/traders/$TRADER_ID/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"exchangeId\":\"$EX_ID\",\"pair\":\"BTCUSDT\",\"side\":\"Buy\",\"type\":\"Market\",\"quantity\":\"0.0001\"}")
ORDER_ID=$(echo $ORDER | jq -r .id)

# 4) 等 60s 让 reconciler 跑一轮，验证 Status 转为 Filled
sleep 65
curl -s $API/api/traders/$TRADER_ID/orders/$ORDER_ID \
  -H "Authorization: Bearer $TOKEN" | jq '{status,exchangeOrderId,filledQuantity}'
# 期望: status=Filled, exchangeOrderId 非空, filledQuantity≈quantity

# 5) 验证 ClientOrderId 透传至交易所（实盘观察）
#    Binance: 网页订单页 / API 返回的 clientOrderId 应等于 order.ClientOrderId.ToString("N")
#    Bybit: orderLinkId 字段
#    OKX: clOrdId 字段
#    Gate: text 字段（"t-" + 26hex 前缀）
#    HTX: client-order-id 字段
```

## 5. 故障演练剧本

每个演练**有期望的恢复行为**。实际行为偏离即为生产风险。

### 5.1 MySQL 断电

```bash
# 演练前：策略已运行，有 Pending 订单（手动构造或等自然产生）
ORDER_BEFORE=$(docker compose exec mysql mysql -uroot -p${MYSQL_ROOT_PASSWORD} -sNe \
  "SELECT COUNT(*) FROM tradex.Orders WHERE Status='Pending'")

# 注入故障
docker compose stop mysql

# 观察 30s：API/Worker 应该
# - Polly EnableRetryOnFailure 应该疯狂重试
# - 风控 ExchangeHealthHandler 应该开始 deny
# - TradingEngine 应该跳过当前 cycle 而不崩溃
docker compose logs --tail 100 backend worker | grep -iE "error|exception|retry" | head -30

# 恢复
docker compose start mysql
sleep 20

# 验证
# 1. API 立即可用
curl -fs $API/health
# 2. Worker 自动恢复（Polly 重连）
docker compose logs --since 1m worker | grep -i "successfully connected" || echo "⚠️ 检查连接重试日志"
# 3. 没丢单（Pending 数量没变化或仅由 Reconciler 推进）
ORDER_AFTER=$(docker compose exec mysql mysql -uroot -p${MYSQL_ROOT_PASSWORD} -sNe \
  "SELECT COUNT(*) FROM tradex.Orders WHERE Status='Pending'")
echo "Pending: $ORDER_BEFORE → $ORDER_AFTER"
```

**期望**：进程不崩；MySQL 起来后自动恢复；订单数量一致；Reconciler 60s 内推进 Pending → Filled/Failed。

### 5.2 Redis 断电

```bash
docker compose stop redis

# 观察 60s
# 期望：
# - 实时 SignalR 推送暂停（前端可能无更新，但 DB 仍是真相）
# - 命令通道暂停 (POST /api/admin/reconcile-now 仍返回 202，但 Worker 不会收到)
# - 业务交易**不受影响**（Polly 重试是 HTTP 层，跟 Redis 无关）
docker compose logs --since 30s backend worker | grep -i redis

docker compose start redis
sleep 10

# 验证订阅自动重建（StackExchange.Redis 内置重连）
docker compose logs --since 1m worker | grep "WorkerCommandSubscriber\|BacktestTaskListener\|订阅成功"
```

**期望**：业务不停；Redis 起来后订阅自动恢复；期间敲的命令会丢失（已知限制，无 Redis Streams + ACK）。

### 5.3 Worker 崩溃 / OOM

```bash
# 演练前：制造一些 Pending 订单（无 ExchangeOrderId 的尤其有意义）
# 通过断网或人为构造（直接 SQL 插入）

docker compose stop worker

# API 仍然能下手动单（API 自身可下单）；但策略评估停止
sleep 30

# 重启 Worker
docker compose start worker

# 观察启动恢复
docker compose logs --tail 100 worker | grep -E "RecoverStuckTasks|启动恢复完成"
# 期望: 看到 "卡死回滚 X 个, Pending 入队 Y 个"

# 60s 后验证 OrderReconciler 跑了一轮
sleep 65
docker compose logs --since 2m worker | grep -E "Reconciliation 完成"
```

**期望**：Worker 重启后自动捡起所有 Pending 任务；OrderReconciler 凭 ClientOrderId 反查恢复无 ExchangeOrderId 的订单。

### 5.4 交易所 API 抖动（5xx）

```bash
# 用 iptables 模拟 Binance API 间歇失败（macOS 用 pfctl，Linux 用 tc/iptables）
# Linux 示例：50% 概率丢包
sudo tc qdisc add dev docker0 root netem loss 50%

# 触发若干次下单
for i in 1 2 3 4 5; do
  curl -sX POST $API/api/traders/$TRADER_ID/orders \
    -H "Authorization: Bearer $TOKEN" \
    -d '{"exchangeId":"...","pair":"BTCUSDT","side":"Buy","type":"Market","quantity":"0.0001"}'
  sleep 2
done

# 观察 Polly 行为
docker compose exec backend curl -s http://localhost/metrics | grep "resilience_polly"
# 关键指标:
# - resilience_polly_strategy_events_total{event_name="OnRetry"} 应增长
# - resilience_polly_strategy_events_total{event_name="OnHalfOpen|OnOpen"} 出现 = 熔断器触发

# 恢复
sudo tc qdisc del dev docker0 root
```

**期望**：GET 路径触发重试（行情数据可恢复）；POST 路径**不重试**（订单不重复）；熔断打开后 15s 自动半开尝试。

### 5.5 时钟漂移

```bash
# 模拟容器时钟前进 5 分钟（部分交易所要求 ±5s 内）
docker compose exec backend date -s "+5 minutes"  # 大多镜像不让改，跳过；用 host 改

# 观察：
# 大多交易所返回 timestamp 错误，Polly 不应该重试这个（4xx 不在重试范围）
# 期望：订单 Mark Failed，明确日志原因
```

**期望**：明确的"timestamp/recv_window 超限"错误；Reconciler 不会卡死。

### 5.6 高并发 / 资源耗尽

见 `tests/loadtest/` k6 脚本（下节）。

## 6. 压测

```bash
cd tests/loadtest
docker run --rm -i --network host grafana/k6 run - < dashboard-load.js
docker run --rm -i --network host grafana/k6 run - < mfa-ops-load.js

# 关键阈值：
# - p99 < 500ms（读路径）
# - errors < 1%
# - throughput ≥ 100 RPS（足够 100 个并发用户）
```

## 7. 备份与恢复演练

```bash
# 备份
docker compose exec mysql mysqldump -uroot -p${MYSQL_ROOT_PASSWORD} \
  --single-transaction --routines --triggers tradex > backup-$(date +%F).sql

# 上传异地（示例 S3）
aws s3 cp backup-$(date +%F).sql s3://tradex-backups/

# 恢复演练（在隔离环境）
docker run --rm -d --name mysql-test -e MYSQL_ROOT_PASSWORD=test mysql:8.4
sleep 30
docker exec -i mysql-test mysql -uroot -ptest < backup-2026-05-16.sql
docker exec mysql-test mysql -uroot -ptest -e "USE tradex; SELECT COUNT(*) FROM Orders;"
```

**最低要求**：每日自动 mysqldump + 7 天保留；每月一次恢复演练，验证 `Orders/Positions/Strategies` 计数与生产一致。

## 8. 回滚

```bash
# Git 回滚到上一个 release tag
git checkout v<previous-tag>

# 重建镜像
docker compose --env-file .env build

# 灰度切换：先 1 个实例
docker compose up -d --no-deps --force-recreate backend

# 验证后切剩余
docker compose up -d --force-recreate
```

**重要**：回滚不会自动反向迁移数据库。如果新版本有 schema 变化，必须额外跑 `dotnet ef migrations remove` 或 down migration。

## 9. 监控告警关键指标

Grafana / Prometheus 应配的告警 rules：

| 告警 | 阈值 | 严重度 |
|------|------|--------|
| `tradex_orders_rejected_total` rate > 10/min | P1 |
| `tradex_risk_denials_total{scope="portfolio"}` rate > 5/min | P2 |
| `tradex_mfa_attempts_total{result!="success"}` rate > 20/min | P1 (可能被暴破) |
| `resilience_polly_strategy_events_total{event_name="OnOpen"}` 出现 | P1 (交易所熔断) |
| `http_server_request_duration_seconds{quantile=0.99}` > 1s | P2 |
| `process_runtime_dotnet_gc_heap_size_bytes` > 1GB 持续 5 min | P3 |
| DB connection pool exhaustion (`mysql` slow log 或 EF retries) | P1 |

---

## 附录：紧急联系流程

1. 严重故障 (P1) → 立即触发 `/api/admin/reconcile-now` + 紧急 `RiskSettings.CircuitBreakerActive=true`
2. 收集 last 1h 全部日志：`docker compose logs --since 1h > incident-$(date +%FT%H%M).log`
3. 通过 RBAC 暂停可疑用户（直接 SQL 改 `Users.Status=Disabled`，立即生效）
4. 数据库快照：`mysqldump ... > pre-rollback.sql`

事故复盘 24h 内输出，至少包含：触发条件 / 影响范围 / 检测延迟 / 恢复时间 / 改进项。
