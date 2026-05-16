#!/usr/bin/env bash
# 故障演练 01: MySQL 短时不可达
#
# 期望行为：
#   - API/Worker 不崩溃
#   - Polly EnableRetryOnFailure 自动重试
#   - MySQL 恢复后业务自动续作，订单计数无变化（除 Reconciler 推进）
#   - Worker 启动恢复扫表（如 Worker 也被影响）

set -euo pipefail
API=${API:-http://localhost}
TOKEN=${TOKEN:?需要 export TOKEN=...}

echo "[CHAOS] === 01: MySQL 短时不可达 ==="

# ---- BASELINE ----
echo "[BASELINE] 快照故障前状态"
ORDERS_BEFORE=$(docker compose exec -T mysql mysql -uroot -p"${MYSQL_ROOT_PASSWORD:-root}" -sNe \
  "SELECT COUNT(*) FROM tradex.Orders" 2>/dev/null || echo "?")
POSITIONS_BEFORE=$(docker compose exec -T mysql mysql -uroot -p"${MYSQL_ROOT_PASSWORD:-root}" -sNe \
  "SELECT COUNT(*) FROM tradex.Positions" 2>/dev/null || echo "?")
HEALTH_BEFORE=$(curl -sof /dev/null -w "%{http_code}" "$API/health" || echo "fail")
echo "  Orders=$ORDERS_BEFORE  Positions=$POSITIONS_BEFORE  /health=$HEALTH_BEFORE"

# ---- INJECT ----
echo "[INJECT] 停止 MySQL 容器"
docker compose stop mysql >/dev/null
INJECT_TIME=$(date +%s)

# ---- OBSERVE ----
echo "[OBSERVE] 30 秒观察期"
sleep 30
RETRY_LOGS=$(docker compose logs --since 30s backend worker 2>/dev/null | grep -ciE "retry|reconnect" || echo 0)
ERROR_LOGS=$(docker compose logs --since 30s backend worker 2>/dev/null | grep -ciE "fatal|panic|stopped" || echo 0)
HEALTH_DURING=$(curl -so /dev/null -w "%{http_code}" --max-time 3 "$API/health" || echo "fail")
echo "  retry 日志: $RETRY_LOGS 行  fatal 日志: $ERROR_LOGS 行  /health 期间=$HEALTH_DURING"

# ---- RESTORE ----
echo "[RESTORE] 启动 MySQL"
docker compose start mysql >/dev/null
echo -n "  等待 MySQL healthy"
for i in {1..30}; do
  if docker compose ps mysql --format json 2>/dev/null | grep -q '"Health":"healthy"'; then
    echo " ✓"; break
  fi
  echo -n "."; sleep 2
done

# 等业务自愈
sleep 20

# ---- VERIFY ----
echo "[VERIFY] 一致性检查"
ORDERS_AFTER=$(docker compose exec -T mysql mysql -uroot -p"${MYSQL_ROOT_PASSWORD:-root}" -sNe \
  "SELECT COUNT(*) FROM tradex.Orders" 2>/dev/null)
POSITIONS_AFTER=$(docker compose exec -T mysql mysql -uroot -p"${MYSQL_ROOT_PASSWORD:-root}" -sNe \
  "SELECT COUNT(*) FROM tradex.Positions" 2>/dev/null)
HEALTH_AFTER=$(curl -so /dev/null -w "%{http_code}" "$API/health" || echo "fail")
echo "  Orders=$ORDERS_AFTER (was $ORDERS_BEFORE)"
echo "  Positions=$POSITIONS_AFTER (was $POSITIONS_BEFORE)"
echo "  /health=$HEALTH_AFTER"

# ---- RESULT ----
FAIL=0
[[ "$HEALTH_AFTER" != "200" ]] && { echo "❌ API 未恢复"; FAIL=1; }
[[ "$ERROR_LOGS" -gt 5 ]] && { echo "❌ fatal 日志过多 ($ERROR_LOGS)"; FAIL=1; }
[[ "$ORDERS_AFTER" -lt "$ORDERS_BEFORE" ]] && { echo "❌ 订单丢失 ($ORDERS_BEFORE → $ORDERS_AFTER)"; FAIL=1; }

if [[ $FAIL -eq 0 ]]; then
  echo "[RESULT] ✅ PASS"
  exit 0
else
  echo "[RESULT] ❌ FAIL"
  exit 1
fi
