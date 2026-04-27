<script setup lang="ts">
import { ref, computed } from 'vue'
import type { BacktestCandleAnalysis } from '../api/backtests'
import BacktestKlineChart from './BacktestKlineChart.vue'
import AppSelect from './AppSelect.vue'
import { formatSmallNumber } from '../utils/format'

const props = defineProps<{
  analysis: BacktestCandleAnalysis[]
  chartOnly?: boolean
  tableOnly?: boolean
}>()

const sortAsc = ref(true)
const filterAction = ref('all')

function fmt(v: unknown): string {
  if (typeof v !== 'number' || isNaN(v)) return '0.00'
  return formatSmallNumber(v)
}

const filtered = computed(() => {
  let list = [...props.analysis]
  if (filterAction.value !== 'all') {
    list = list.filter(a => a.action === filterAction.value)
  }
  list.sort((a, b) => sortAsc.value ? a.index - b.index : b.index - a.index)
  return list
})

const indicatorKeys = computed(() => {
  if (props.analysis.length === 0) return []
  const first = props.analysis[0]
  if (!first || !first.indicators) return []
  return Object.keys(first.indicators)
})

const actionLabels: Record<string, string> = {
  none: '无操作', enter: '入场', exit: '出场'
}
const actionColors: Record<string, string> = {
  none: '#64748b', enter: 'var(--accent-green)', exit: '#ef4444'
}
</script>

<template>
  <div class="analysis-section">
    <BacktestKlineChart v-if="!tableOnly" :analysis="analysis" />

    <div v-if="!chartOnly">
      <div class="analysis-toolbar">
        <div class="toolbar-left">
          <span class="toolbar-label">共 {{ analysis.length }} 根 K 线</span>
          <span class="toolbar-divider">|</span>
          <label class="toolbar-label">筛选</label>
          <AppSelect
            :options="[
              { label: '全部', value: 'all' },
              { label: '仅入场', value: 'enter' },
              { label: '仅出场', value: 'exit' },
            ]"
            :model-value="filterAction"
            @update:model-value="(v: string | number) => filterAction = String(v)"
          />
        </div>
        <button class="sort-btn" @click="sortAsc = !sortAsc">
          {{ sortAsc ? '↑ 正序' : '↓ 倒序' }}
        </button>
      </div>

      <div class="analysis-table-wrap">
      <table class="analysis-table">
        <thead>
          <tr>
            <th>#</th>
            <th>时间</th>
            <th>开盘</th>
            <th>最高</th>
            <th>最低</th>
            <th>收盘</th>
            <th>成交量</th>
            <th v-for="k in indicatorKeys" :key="k" class="indicator-cell">{{ k }}</th>
            <th>持仓</th>
            <th>入场价值</th>
            <th>当前价值</th>
            <th>盈亏金额</th>
            <th>盈亏比</th>
            <th>动作</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="a in filtered"
            :key="a.index"
            class="analysis-row"
            :class="{
              'row-enter': a.action === 'enter',
              'row-exit': a.action === 'exit',
              'row-in-position': a.inPosition
            }"
          >
            <td class="cell-index">{{ a.index }}</td>
            <td class="cell-date">{{ new Date(a.timestamp).toLocaleString() }}</td>
            <td class="market-cell" :title="String(a.open)">{{ fmt(a.open) }}</td>
            <td class="market-cell high-cell" :title="String(a.high)">{{ fmt(a.high) }}</td>
            <td class="market-cell low-cell" :title="String(a.low)">{{ fmt(a.low) }}</td>
            <td class="cell-close" :title="String(a.close)">{{ fmt(a.close) }}</td>
            <td class="market-cell volume-cell" :title="String(a.volume)">{{ fmt(a.volume) }}</td>
            <td v-for="k in indicatorKeys" :key="k" class="indicator-value" :title="String(a.indicators[k] ?? 0)">{{ fmt(a.indicators[k] ?? 0) }}</td>
            <td>
              <span :class="a.inPosition ? 'badge-in' : 'badge-out'">
                {{ a.inPosition ? '持仓' : '空仓' }}
              </span>
            </td>
            <td :title="String(a.positionCost ?? 0)">{{ a.positionCost ? `$${fmt(a.positionCost)}` : '-' }}</td>
            <td :title="String(a.positionValue ?? 0)">{{ a.positionValue ? `$${fmt(a.positionValue)}` : '-' }}</td>
            <td>
              <span v-if="a.taskPnl !== null && a.taskPnl !== undefined" :class="a.taskPnl >= 0 ? 'pnl-up' : 'pnl-down'">
                {{ a.taskPnl >= 0 ? '+' : '' }}${{ fmt(a.taskPnl) }}
              </span>
              <span v-else>-</span>
            </td>
            <td>
              <span v-if="a.taskPnlPercent !== null && a.taskPnlPercent !== undefined" :class="a.taskPnlPercent >= 0 ? 'pnl-up' : 'pnl-down'">
                {{ a.taskPnlPercent >= 0 ? '+' : '' }}{{ fmt(a.taskPnlPercent) }}%
              </span>
              <span v-else>-</span>
            </td>
            <td>
              <span class="action-badge" :style="{ background: actionColors[a.action] }">
                {{ actionLabels[a.action] || a.action }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</div>
</template>

<style scoped>
.analysis-section { margin-top: 1rem; }
.analysis-toolbar {
  display: flex; align-items: center; justify-content: space-between;
  margin-bottom: 0.5rem; gap: 0.75rem;
}
.toolbar-left { display: flex; align-items: center; gap: 0.5rem; }
.toolbar-label { color: var(--text-muted); font-size: 0.8rem; }
.toolbar-divider { color: #334155; }
.sort-btn {
  padding: 0.25rem 0.5rem; background: rgba(255,255,255,0.55); color: var(--text-muted);
  border: 1px solid var(--glass-border); border-radius: 4px; cursor: pointer; font-size: 0.8rem;
}
.sort-btn:hover { color: var(--text-primary); }

.analysis-table-wrap { overflow-x: auto; max-height: 480px; overflow-y: auto; border: 1px solid var(--glass-border); border-radius: 6px; }
.analysis-table { width: 100%; border-collapse: collapse; font-size: 0.75rem; white-space: nowrap; }
.analysis-table th, .analysis-table td {
  padding: 0.375rem 0.5rem; text-align: right; border-bottom: 1px solid rgba(100, 116, 139, 0.16);
  color: #334155; font-family: 'SF Mono', 'Fira Code', monospace;
}
.analysis-table th {
  position: sticky; top: 0; z-index: 1;
  background: rgba(242, 246, 252, 0.96); color: #334155; font-weight: 700;
  text-align: right; border-bottom: 1px solid rgba(79, 126, 201, 0.18);
  box-shadow: 0 1px 0 rgba(255,255,255,0.8) inset, 0 4px 12px rgba(15,23,42,0.06);
}
.analysis-table thead th:first-child,
.analysis-table tbody td:first-child { text-align: center; }
.analysis-table th.indicator-cell, .analysis-table td.indicator-value { color: var(--accent-blue); }
.analysis-row { transition: background 0.1s; }
.analysis-row:hover { background: rgba(79, 126, 201, 0.04); }
.row-enter { background: rgba(34, 197, 94, 0.06); }
.row-enter:hover { background: rgba(34, 197, 94, 0.1); }
.row-exit { background: rgba(239, 68, 68, 0.06); }
.row-exit:hover { background: rgba(239, 68, 68, 0.1); }
.row-in-position td { color: var(--text-primary); }

.cell-index { color: var(--text-muted); }
.cell-date { color: #0f172a; text-align: left !important; min-width: 130px; font-weight: 650; }
.market-cell { color: #111827; font-weight: 650; }
.high-cell { color: #047857; font-weight: 700; }
.low-cell { color: #dc2626; font-weight: 700; }
.volume-cell { color: #1f2937; font-weight: 650; }
.cell-close { color: #020617; font-weight: 800; }

.badge-in, .badge-out {
  display: inline-block; padding: 0.0625rem 0.375rem; border-radius: 999px;
  font-size: 0.65rem; font-weight: 600;
}
.badge-in { background: rgba(34, 197, 94, 0.15); color: var(--accent-green); }
.badge-out { background: rgba(100, 116, 139, 0.15); color: var(--text-muted); }
.pnl-up { color: var(--accent-green); font-weight: 600; }
.pnl-down { color: var(--accent-red); font-weight: 600; }

.action-badge {
  display: inline-block; padding: 0.0625rem 0.375rem; border-radius: 999px;
  color: var(--text-primary); font-size: 0.65rem; font-weight: 600;
}
</style>
