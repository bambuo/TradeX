<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, nextTick, watch } from 'vue'
import type { BacktestCandleAnalysis } from '../api/backtests'
import BacktestKlineChart from './BacktestKlineChart.vue'
import { formatSmallNumber } from '../utils/format'

const props = defineProps<{
  analysis: BacktestCandleAnalysis[]
  chartOnly?: boolean
  tableOnly?: boolean
  currentIndex?: number
}>()

const sortAsc = ref(true)

function fmt(v: unknown): string {
  if (typeof v !== 'number' || isNaN(v)) return '0.00'
  return formatSmallNumber(v)
}

const sorted = computed(() => {
  return [...props.analysis].sort((a, b) => sortAsc.value ? a.index - b.index : b.index - a.index)
})

const indicatorKeys = computed(() => {
  if (props.analysis.length === 0) return []
  const first = props.analysis[0]
  if (!first || !first.indicators) return []
  return Object.keys(first.indicators)
})

const actionLabels: Record<string, string> = {
  none: '-', enter: '入场', exit: '出场'
}

const expandedIndex = ref<number | null>(null)

function toggleExpand(index: number) {
  expandedIndex.value = expandedIndex.value === index ? null : index
}

const tableWrapRef = ref<HTMLDivElement>()
let stickyObserver: ResizeObserver | null = null

function updateStickyRight() {
  const wrap = tableWrapRef.value
  if (!wrap) return
  const table = wrap.querySelector('table')
  if (!table) return
  const headerRow = table.querySelector('thead tr')
  if (!headerRow) return

  const cells = headerRow.querySelectorAll('th')
  if (cells.length < 3) return

  const w1 = (cells[cells.length - 1] as HTMLElement).offsetWidth
  const w2 = (cells[cells.length - 2] as HTMLElement).offsetWidth

  for (const row of table.querySelectorAll('tr')) {
    const rowCells = row.querySelectorAll('th, td')
    if (rowCells.length < 3) continue
    ;(rowCells[rowCells.length - 3] as HTMLElement).style.right = `${w1 + w2}px`
    ;(rowCells[rowCells.length - 2] as HTMLElement).style.right = `${w1}px`
    ;(rowCells[rowCells.length - 1] as HTMLElement).style.right = '0px'
  }
}

onMounted(() => {
  nextTick(updateStickyRight)
  if (tableWrapRef.value) {
    stickyObserver = new ResizeObserver(updateStickyRight)
    stickyObserver.observe(tableWrapRef.value)
  }
})

onUnmounted(() => {
  stickyObserver?.disconnect()
})

watch(() => props.analysis?.length, () => nextTick(updateStickyRight))
</script>

<template>
  <div class="analysis-section">
    <BacktestKlineChart v-if="!tableOnly" :analysis="analysis" :current-index="currentIndex" />

    <div v-if="!chartOnly">
      <div class="analysis-toolbar">
        <div class="toolbar-left">
          <span class="toolbar-label">共 {{ analysis.length }} 根 K 线</span>
        </div>
        <button class="sort-btn" @click="sortAsc = !sortAsc">
          {{ sortAsc ? '↑ 正序' : '↓ 倒序' }}
        </button>
      </div>

      <div ref="tableWrapRef" class="analysis-table-wrap">
      <table class="analysis-table">
        <thead>
          <tr>
            <th>#</th>
            <th>时间</th>
            <th>持仓</th>
            <th>入场价值</th>
            <th>当前价值</th>
            <th>盈亏金额</th>
            <th>盈亏比</th>
            <th>动作</th>
          </tr>
        </thead>
        <tbody>
          <template v-for="a in sorted" :key="a.index">
            <tr
              class="analysis-row"
              :class="{
                'row-enter': a.action === 'enter',
                'row-exit': a.action === 'exit',
                'row-in-position': a.inPosition,
                'row-expanded': expandedIndex === a.index
              }"
              @click="toggleExpand(a.index)"
            >
              <td class="cell-index">{{ a.index }}</td>
              <td class="cell-date">{{ new Date(a.timestamp).toLocaleString() }}</td>
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
                <span :class="a.action === 'enter' ? 'text-enter' : a.action === 'exit' ? 'text-exit' : ''">
                  {{ actionLabels[a.action] || actionLabels.none }}
                </span>
              </td>
            </tr>
            <tr v-if="expandedIndex === a.index" class="detail-row">
              <td colspan="8" class="detail-cell">
                <div class="detail-grid">
                  <div class="detail-group">
                    <div class="detail-group-title">K 线数据</div>
                    <div class="detail-group-body">
                      <span class="detail-item"><span class="detail-label">开盘</span><span class="detail-value">{{ fmt(a.open) }}</span></span>
                      <span class="detail-item"><span class="detail-label">最高</span><span class="detail-value up">{{ fmt(a.high) }}</span></span>
                      <span class="detail-item"><span class="detail-label">最低</span><span class="detail-value down">{{ fmt(a.low) }}</span></span>
                      <span class="detail-item"><span class="detail-label">收盘</span><span class="detail-value">{{ fmt(a.close) }}</span></span>
                      <span class="detail-item"><span class="detail-label">成交量</span><span class="detail-value">{{ fmt(a.volume) }}</span></span>
                    </div>
                  </div>
                  <div v-if="indicatorKeys.length > 0" class="detail-group">
                    <div class="detail-group-title">技术指标</div>
                    <div class="detail-group-body">
                      <span v-for="k in indicatorKeys" :key="k" class="detail-item">
                        <span class="detail-label">{{ k }}</span>
                        <span class="detail-value indicator">{{ fmt(a.indicators[k] ?? 0) }}</span>
                      </span>
                    </div>
                  </div>
                </div>
              </td>
            </tr>
          </template>
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

.analysis-table td:nth-last-child(-n+3) {
  position: sticky;
  z-index: 2;
  background: rgba(255, 255, 255, 0.95);
}

.analysis-table th:nth-last-child(-n+3) {
  z-index: 3;
}

.analysis-row {
  cursor: pointer;
  transition: background 0.1s;
}

.analysis-row:hover { background: rgba(79, 126, 201, 0.04); }
.row-enter { background: rgba(34, 197, 94, 0.06); }
.row-enter:hover { background: rgba(34, 197, 94, 0.1); }
.row-exit { background: rgba(239, 68, 68, 0.06); }
.row-exit:hover { background: rgba(239, 68, 68, 0.1); }
.row-in-position td { color: var(--text-primary); }

.detail-row td {
  padding: 0;
  background: rgba(242, 246, 252, 0.5);
  border-bottom: 1px solid rgba(100, 116, 139, 0.16);
}

.detail-cell {
  position: static !important;
}

.detail-grid {
  padding: 0.5rem 0.75rem;
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 0.75rem;
}

.detail-group {
  background: rgba(255, 255, 255, 0.6);
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  padding: 0.5rem 0.625rem;
  min-width: 0;
}

.detail-group-title {
  color: var(--text-muted);
  font-size: 0.65rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 0.125rem;
}

.detail-group-body {
  display: grid;
  grid-template-rows: repeat(5, auto);
  grid-auto-flow: column;
  gap: 0.125rem 0.75rem;
}

.detail-item {
  display: grid;
  gap: 0;
}

.detail-group:first-child .detail-item {
  grid-template-columns: 6rem 1fr;
}

.detail-group + .detail-group .detail-item {
  grid-template-columns: 4.5rem 1fr;
}

.detail-label {
  text-align: right;
  color: var(--text-muted);
  font-size: 0.72rem;
  font-weight: 500;
}

.detail-value {
  text-align: right;
  padding-left: 0.5rem;
  color: #334155;
  font-size: 0.72rem;
  font-weight: 600;
  font-family: 'SF Mono', 'Fira Code', monospace;
}

.detail-value.up { color: var(--accent-green); }
.detail-value.down { color: var(--accent-red); }
.detail-value.indicator { color: var(--accent-blue); }

.cell-index { color: var(--text-muted); }
.cell-date { color: #0f172a; text-align: left !important; min-width: 130px; font-weight: 650; }

.badge-in, .badge-out {
  display: inline-block; padding: 0.0625rem 0.375rem; border-radius: 999px;
  font-size: 0.65rem; font-weight: 600;
}
.badge-in { background: rgba(34, 197, 94, 0.15); color: var(--accent-green); }
.badge-out { background: rgba(100, 116, 139, 0.15); color: var(--text-muted); }
.pnl-up { color: var(--accent-green); font-weight: 600; }
.pnl-down { color: var(--accent-red); font-weight: 600; }

.text-enter { color: var(--accent-green); font-weight: 600; }
.text-exit { color: #ef4444; font-weight: 600; }
</style>
