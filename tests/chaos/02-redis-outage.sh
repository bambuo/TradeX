#!/usr/bin/env bash
# 故障演练 02: Redis 短时不可达
#
# 期望行为：
#   - 业务交易不受影响（Redis 故障仅影响实时事件 + 命令通道）
#   - SignalR 推送暂停，前端可能感知不到状态变化，但 DB 仍是真相
#   - StackExchange.Redis 内置重连，Redis 起来后订阅自动重建
#   - 期间命令通道命令丢失（已知限制：Pub/Sub 无 ACK）

set -euo pipefail
API=${API:-http://localhost}

echo "[CHAOS] === 02: Redis 短时不可达 ==="

# ---- BASELINE ----
echo "[BASELINE] 订阅状态"
CHANNELS_BEFORE=$(docker compose exec -T redis redis-cli PUBSUB CHANNELS 'tradex:*' 2>/dev/null | wc -l)
HEALTH_BEFORE=$(curl -so /dev/null -w "%{http_code}" "$API/health" || echo "fail")
echo "  tradex:* 订阅频道数=$CHANNELS_BEFORE  /health=$HEALTH_BEFORE"

# ---- INJECT ----
echo "[INJECT] 停止 Redis 容器"
docker compose stop redis >/dev/null

# ---- OBSERVE ----
echo "[OBSERVE] 30 秒观察"
sleep 30
ERROR_LOGS=$(docker compose logs --since 30s backend worker 2>/dev/null | grep -ciE "fatal|stopped|crashed" || echo 0)
REDIS_ERRORS=$(docker compose logs --since 30s backend worker 2>/dev/null | grep -ciE "RedisConnectionException|UnableToConnect" || echo 0)
HEALTH_DURING=$(curl -so /dev/null -w "%{http_code}" --max-time 3 "$API/health" || echo "fail")
echo "  fatal 日志: $ERROR_LOGS  redis 重连日志: $REDIS_ERRORS  /health=$HEALTH_DURING"

# ---- RESTORE ----
echo "[RESTORE] 启动 Redis"
docker compose start redis >/dev/null
echo -n "  等待 Redis healthy"
for i in {1..15}; do
  if docker compose exec -T redis redis-cli PING 2>/dev/null | grep -q PONG; then
    echo " ✓"; break
  fi
  echo -n "."; sleep 2
done
sleep 15

# ---- VERIFY ----
echo "[VERIFY] 订阅自动重建"
CHANNELS_AFTER=$(docker compose exec -T redis redis-cli PUBSUB CHANNELS 'tradex:*' 2>/dev/null | wc -l)
HEALTH_AFTER=$(curl -so /dev/null -w "%{http_code}" "$API/health" || echo "fail")
echo "  tradex:* 订阅频道数=$CHANNELS_AFTER (was $CHANNELS_BEFORE)"
echo "  /health=$HEALTH_AFTER"

# ---- RESULT ----
FAIL=0
[[ "$HEALTH_AFTER" != "200" ]] && { echo "❌ API 未恢复"; FAIL=1; }
[[ "$ERROR_LOGS" -gt 5 ]] && { echo "❌ fatal 日志过多 ($ERROR_LOGS)"; FAIL=1; }
[[ "$CHANNELS_AFTER" -lt 2 ]] && { echo "⚠️ 订阅频道数偏少（可能 Listener 未重连）"; FAIL=1; }

if [[ $FAIL -eq 0 ]]; then
  echo "[RESULT] ✅ PASS"
  exit 0
else
  echo "[RESULT] ❌ FAIL"
  exit 1
fi
