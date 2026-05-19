<script setup lang="ts">
import type { BacktestKlineAnalysis } from '../api/backtests'
import { formatSmallNumber } from '../utils/format'

const props = defineProps<{
  item: BacktestKlineAnalysis | null
  total?: number
  current?: number
}>()

function fmt(v: unknown): string {
  if (typeof v !== 'number' || isNaN(v)) return '0.00'
  return formatSmallNumber(v)
}

const actionLabels: Record<string, string> = {
  none: '-', enter: '入场', exit: '出场'
}

function usd(v: number | null | undefined): string {
  if (v == null || isNaN(v)) return '-'
  return `$${fmt(v)}`
}
</script>

<template>
  <div v-if="item" class="cursor-info-panel">
    <div class="info-header">
      <span class="info-title">K 线 #{{ item.index }}<template v-if="total"> / {{ total }}</template></span>
      <span class="info-time">{{ new Date(item.timestamp).toLocaleString('zh-CN') }}</span>
    </div>
    <div class="info-body">
      <div class="info-section">
        <div class="info-section-title">K 线数据</div>
        <div class="info-grid">
          <div class="info-cell">
            <span class="info-label">开盘</span>
            <span class="info-value">{{ fmt(item.open) }}</span>
          </div>
          <div class="info-cell">
            <span class="info-label">最高</span>
            <span class="info-value up">{{ fmt(item.high) }}</span>
          </div>
          <div class="info-cell">
            <span class="info-label">最低</span>
            <span class="info-value down">{{ fmt(item.low) }}</span>
          </div>
          <div class="info-cell">
            <span class="info-label">收盘</span>
            <span class="info-value">{{ fmt(item.close) }}</span>
          </div>
          <div class="info-cell">
            <span class="info-label">成交量</span>
            <span class="info-value">{{ fmt(item.volume) }}</span>
          </div>
        </div>
      </div>

      <div class="info-section">
        <div class="info-section-title">
          持仓状态
          <a-tag :color="item.inPosition ? 'green' : ''" size="small" style="margin-left: 4px">
            {{ item.inPosition ? '持仓中' : '空仓' }}
          </a-tag>
        </div>
        <div class="info-grid">
          <div class="info-cell">
            <span class="info-label">动作</span>
            <span class="info-value" :class="item.action === 'enter' ? 'up' : item.action === 'exit' ? 'down' : ''">
              {{ actionLabels[item.action] || '-' }}
            </span>
          </div>
          <div class="info-cell">
            <span class="info-label">入场价</span>
            <span class="info-value">{{ item.avgEntryPrice != null ? usd(item.avgEntryPrice) : '-' }}</span>
          </div>
          <div class="info-cell">
            <span class="info-label">持仓量</span>
            <span class="info-value">{{ item.positionQuantity != null ? fmt(item.positionQuantity) : '-' }}</span>
          </div>
          <div class="info-cell">
            <span class="info-label">持仓价值</span>
            <span class="info-value">{{ item.positionValue != null ? usd(item.positionValue) : '-' }}</span>
          </div>
          <div class="info-cell">
            <span class="info-label">入场条件</span>
            <span class="info-value" :class="item.entry === true ? 'up' : item.entry === false ? 'down' : ''">
              {{ item.entry === true ? '触发' : item.entry === false ? '未触发' : '-' }}
            </span>
          </div>
          <div class="info-cell">
            <span class="info-label">出场条件</span>
            <span class="info-value" :class="item.exit === true ? 'up' : item.exit === false ? 'down' : ''">
              {{ item.exit === true ? '触发' : item.exit === false ? '未触发' : '-' }}
            </span>
          </div>
        </div>
        <div v-if="item.inPosition" class="info-pnl">
          <span class="pnl-item" :class="(item.positionPnl ?? 0) >= 0 ? 'up' : 'down'">
            浮动盈亏: {{ (item.positionPnl ?? 0) >= 0 ? '+' : '' }}{{ usd(item.positionPnl) }}
          </span>
          <span class="pnl-item" :class="(item.positionPnlPercent ?? 0) >= 0 ? 'up' : 'down'">
            收益率: {{ (item.positionPnlPercent ?? 0) >= 0 ? '+' : '' }}{{ fmt(item.positionPnlPercent) }}%
          </span>
        </div>
      </div>

      <div v-if="item.indicators && Object.keys(item.indicators).length > 0" class="info-section">
        <div class="info-section-title">技术指标</div>
        <div class="info-grid">
          <div v-for="(val, key) in item.indicators" :key="key" class="info-cell">
            <span class="info-label">{{ key.toUpperCase() }}</span>
            <span class="info-value indicator">{{ fmt(val) }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
  <div v-else-if="total && total > 0" class="cursor-info-panel info-empty">
    点击播放或在图表上悬停查看 K 线详情
  </div>
</template>

<style scoped>
.cursor-info-panel {
  background: rgba(255, 255, 255, 0.7);
  border: 1px solid var(--glass-border);
  border-radius: 8px;
  margin-top: 0.5rem;
  overflow: hidden;
}
.info-empty {
  padding: 1.5rem 1rem;
  text-align: center;
  color: var(--text-muted);
  font-size: 0.8rem;
}
.info-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.5rem 0.75rem;
  background: rgba(242, 246, 252, 0.8);
  border-bottom: 1px solid rgba(100, 116, 139, 0.12);
}
.info-title {
  font-weight: 700;
  font-size: 0.8rem;
  color: var(--text-primary);
}
.info-time {
  font-size: 0.75rem;
  color: var(--text-muted);
  font-family: 'SF Mono', 'Fira Code', monospace;
}
.info-body {
  padding: 0.5rem 0.75rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}
.info-section {
  background: rgba(255, 255, 255, 0.5);
  border: 1px solid rgba(100, 116, 139, 0.1);
  border-radius: 6px;
  padding: 0.5rem;
}
.info-section-title {
  font-size: 0.65rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  color: var(--text-muted);
  margin-bottom: 0.375rem;
  display: flex;
  align-items: center;
}
.info-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(120px, 1fr));
  gap: 0.25rem 0.5rem;
}
.info-cell {
  display: flex;
  flex-direction: column;
  gap: 0;
}
.info-label {
  font-size: 0.65rem;
  color: var(--text-muted);
  font-weight: 500;
}
.info-value {
  font-size: 0.8rem;
  font-weight: 600;
  color: #334155;
  font-family: 'SF Mono', 'Fira Code', monospace;
}
.info-value.up { color: var(--accent-green); }
.info-value.down { color: var(--accent-red); }
.info-value.indicator { color: var(--accent-blue); }
.info-pnl {
  margin-top: 0.375rem;
  display: flex;
  gap: 1rem;
  font-size: 0.75rem;
  font-weight: 600;
  font-family: 'SF Mono', 'Fira Code', monospace;
}
.pnl-item.up { color: var(--accent-green); }
.pnl-item.down { color: var(--accent-red); }
</style>
