# TradeX 压测套件

基于 [k6](https://k6.io/) 的负载与性能验证脚本。

## 准备

```bash
# 1) 后端栈正常运行
docker compose ps

# 2) 准备一个有效的 JWT token（不带 MFA 操作的脚本只需 token）
export API=http://localhost
export TOKEN=$(curl -sX POST $API/api/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"<pwd>"}' | jq -r .accessToken)
```

## 脚本

| 脚本 | 目标 | 通过标准 |
|------|------|---------|
| `dashboard-load.js` | Dashboard + Positions + Orders 读路径 | p99 < 500ms, error rate < 1% |
| `auth-load.js` | 登录吞吐 | p95 < 300ms, ≥ 50 RPS |
| `signalr-fanout.js` | SignalR 多用户广播 | 1000 并发连接稳定 |

## 运行

```bash
cd tests/loadtest

# Docker 跑（推荐，无需本地安装 k6）
docker run --rm -i --network host \
  -e API=$API -e TOKEN=$TOKEN \
  grafana/k6 run - < dashboard-load.js

# 或本地 k6
k6 run dashboard-load.js -e API=$API -e TOKEN=$TOKEN
```

## 输出

k6 默认输出到 stdout。生产建议导出 Prometheus：

```bash
docker run --rm -i --network host \
  -e API=$API -e TOKEN=$TOKEN \
  -e K6_PROMETHEUS_RW_SERVER_URL=http://localhost:9090/api/v1/write \
  grafana/k6 run -o experimental-prometheus-rw - < dashboard-load.js
```

然后在 Grafana 查 `k6_http_req_duration_*` 指标。

## 推荐压测节奏

部署后必跑：
1. **冒烟**：`dashboard-load.js` 跑 1 分钟，验证基线（参见 `RUNBOOK.md §3`）
2. **负载**：`dashboard-load.js` 默认配置（5 分钟，递增到 100 VU）
3. **拐点**：把 `stages` 改为递增到 500 VU 找性能拐点

每次发布前回归：
- 跑相同脚本 → 与上次结果对比 → 任何指标恶化 >20% 时 block 发版
