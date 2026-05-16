#!/usr/bin/env bash
# 故障演练 04: 交易所 API 抖动（30% 丢包）
#
# 仅在 Linux 上工作（依赖 tc + netem）。macOS 上跳过。
#
# 期望行为：
#   - GET 读路径触发 Polly 重试（行情/订单查询应恢复）
#   - POST 写路径不重试（订单不重复下单）
#   - 熔断器在错误率 >50% 时打开
#   - Prometheus 应记录 resilience_polly_* 指标增长

set -euo pipefail
API=${API:-http://localhost}

echo "[CHAOS] === 04: 交易所 API 抖动 ==="

if [[ "$(uname)" != "Linux" ]]; then
  echo "⚠️ macOS 无 tc/netem，跳过此演练。可在 Linux staging 上跑。"
  exit 0
fi

if ! command -v tc >/dev/null 2>&1; then
  echo "⚠️ 缺 tc 工具，跳过。安装: apt install iproute2"
  exit 0
fi

DEV=${CHAOS_DEV:-docker0}
LOSS=${CHAOS_LOSS:-30%}

# ---- BASELINE ----
RETRY_BEFORE=$(curl -s "$API/metrics" | grep -E "^resilience_polly_strategy_events_total.*OnRetry" | awk '{print $NF}' | head -1)
RETRY_BEFORE=${RETRY_BEFORE:-0}
echo "[BASELINE] Polly OnRetry count=$RETRY_BEFORE"

# ---- INJECT ----
echo "[INJECT] tc qdisc 在 $DEV 设 $LOSS 丢包"
sudo tc qdisc add dev $DEV root netem loss $LOSS

cleanup() {
  echo "[RESTORE] 清除 tc 规则"
  sudo tc qdisc del dev $DEV root 2>/dev/null || true
}
trap cleanup EXIT

# ---- OBSERVE ----
echo "[OBSERVE] 等待业务自然访问交易所（2 分钟）"
echo "  期间策略会调用 GetKlinesAsync 等接口，Polly 应自动重试"
sleep 120

RETRY_AFTER=$(curl -s "$API/metrics" | grep -E "^resilience_polly_strategy_events_total.*OnRetry" | awk '{print $NF}' | head -1)
RETRY_AFTER=${RETRY_AFTER:-0}
CB_OPEN=$(curl -s "$API/metrics" | grep -E "^resilience_polly_strategy_events_total.*OnOpen" | awk '{print $NF}' | head -1)
CB_OPEN=${CB_OPEN:-0}
echo "[VERIFY] Polly OnRetry: $RETRY_BEFORE → $RETRY_AFTER"
echo "         Circuit OnOpen: $CB_OPEN（>0 表示熔断触发）"

# ---- RESULT ----
DELTA=$((RETRY_AFTER - RETRY_BEFORE))
FAIL=0
if [[ $DELTA -lt 3 ]]; then
  echo "❌ 重试增量过少 ($DELTA)，Polly 未生效或网络丢包未达交易所"
  FAIL=1
fi

if [[ $FAIL -eq 0 ]]; then
  echo "[RESULT] ✅ PASS — Polly 重试 $DELTA 次"
  exit 0
else
  echo "[RESULT] ❌ FAIL"
  exit 1
fi
