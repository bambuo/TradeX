#!/usr/bin/env bash
# 故障演练 03: Worker 进程崩溃
#
# 期望行为：
#   - API 仍可下手动单（API 自身有 TradeExecutor）
#   - 策略评估暂停（直到 Worker 重启）
#   - 重启后 RecoverStuckTasksAsync 把 Running 任务回滚 Pending 并入队
#   - OrderReconciler 在 ~60s 内跑一轮，凭 ClientOrderId 修复挂起订单

set -euo pipefail
API=${API:-http://localhost}

echo "[CHAOS] === 03: Worker 进程崩溃 ==="

# ---- BASELINE ----
PENDING_BEFORE=$(docker compose exec -T mysql mysql -uroot -p"${MYSQL_ROOT_PASSWORD:-root}" -sNe \
  "SELECT COUNT(*) FROM tradex.Orders WHERE Status='Pending'" 2>/dev/null || echo 0)
RUNNING_BEFORE=$(docker compose exec -T mysql mysql -uroot -p"${MYSQL_ROOT_PASSWORD:-root}" -sNe \
  "SELECT COUNT(*) FROM tradex.BacktestTasks WHERE Status='Running'" 2>/dev/null || echo 0)
echo "[BASELINE] Orders.Pending=$PENDING_BEFORE  BacktestTasks.Running=$RUNNING_BEFORE"

# ---- INJECT ----
echo "[INJECT] kill Worker 容器（SIGKILL，模拟 OOM）"
docker compose kill -s SIGKILL worker >/dev/null

# ---- OBSERVE ----
echo "[OBSERVE] Worker 关闭 20s 内 API 应仍可用"
sleep 20
HEALTH_DURING=$(curl -so /dev/null -w "%{http_code}" --max-time 3 "$API/health" || echo "fail")
echo "  /health=$HEALTH_DURING"

# ---- RESTORE ----
echo "[RESTORE] 启动 Worker"
docker compose start worker >/dev/null
echo -n "  等待 Worker 日志出现 '已装载'"
for i in {1..30}; do
  if docker compose logs --tail 50 worker 2>/dev/null | grep -q "已装载 TradingEngine"; then
    echo " ✓"; break
  fi
  echo -n "."; sleep 2
done

# ---- VERIFY 启动恢复 ----
echo "[VERIFY] 启动恢复"
sleep 10
RECOVER_LOG=$(docker compose logs --since 30s worker 2>/dev/null | grep -E "启动恢复完成|RecoverStuckTasks" | head -3)
echo "  $RECOVER_LOG"

# ---- VERIFY OrderReconciler 跑了一轮 ----
echo "[VERIFY] 等待 OrderReconciler 第一轮（默认 60s 间隔）"
sleep 70
RECONCILE_LOG=$(docker compose logs --since 90s worker 2>/dev/null | grep -E "Reconciliation 完成" | tail -3)
echo "  $RECONCILE_LOG"

# ---- RESULT ----
FAIL=0
[[ "$HEALTH_DURING" != "200" ]] && { echo "❌ Worker 挂时 API 也挂了，故障域隔离失败"; FAIL=1; }
[[ -z "$RECOVER_LOG" ]] && { echo "❌ 未观察到启动恢复日志"; FAIL=1; }
[[ -z "$RECONCILE_LOG" ]] && { echo "⚠️ 未观察到 Reconciliation 完成日志（90s 内）"; FAIL=1; }

if [[ $FAIL -eq 0 ]]; then
  echo "[RESULT] ✅ PASS"
  exit 0
else
  echo "[RESULT] ❌ FAIL"
  exit 1
fi
