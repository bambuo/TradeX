# TradeX 故障演练剧本

> ⚠️ **生产环境禁止运行**。这些脚本会主动停止服务，专为预生产 / staging 环境的混沌测试设计。

## 使用前提

1. `docker compose up -d` 全栈正常运行
2. 至少有一个有效的 admin JWT token（设到 `$TOKEN`）
3. 准备好观察日志的另一个终端：`docker compose logs -f`

## 脚本

| 序号 | 文件 | 演练场景 | 期望恢复时间 |
|------|------|---------|----------|
| 01 | `01-mysql-outage.sh` | MySQL 短时不可达 | < 60s |
| 02 | `02-redis-outage.sh` | Redis 短时不可达 | < 30s |
| 03 | `03-worker-crash.sh` | Worker 进程崩溃 | < 90s (含 Reconciler 一轮) |
| 04 | `04-exchange-jitter.sh` | 模拟交易所 API 抖动（需 Linux + iptables） | 立即（Polly 自动应对） |

## 通用模板

每个脚本输出以下信息：

```
[CHAOS] === <演练名> ===
[BASELINE] <故障前关键指标快照>
[INJECT] <故障注入命令>
[OBSERVE] <30-60s 观察期 + 日志摘录>
[RESTORE] <恢复命令>
[VERIFY] <一致性 / 可达性检查>
[RESULT] PASS | FAIL（带原因）
```

## 完整演练流程（推荐）

```bash
cd tests/chaos
export TOKEN=...
export API=http://localhost

for drill in 01-mysql-outage.sh 02-redis-outage.sh 03-worker-crash.sh; do
  echo "==================================="
  bash $drill || { echo "❌ $drill 失败，停止后续演练"; exit 1; }
  echo "等待 30s 让系统回稳..."
  sleep 30
done
echo "✅ 全部演练通过"
```

每个 PR 发版前应至少跑一次 01 + 03（最关键的两项）。
