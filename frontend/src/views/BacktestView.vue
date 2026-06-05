<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { backtestsApi, type BacktestTask, type BacktestResult, type BacktestKlineAnalysis } from '../api/backtests'
import BacktestKlineAnalysisView from '../components/BacktestKlineAnalysis.vue'
import BacktestKlineCursorInfo from '../components/BacktestKlineCursorInfo.vue'

const route = useRoute()
const taskId = route.params.taskId as string

const task = ref<BacktestTask | null>(null)
const result = ref<BacktestResult | null>(null)
const loading = ref(true)
const error = ref('')
const activeTab = ref<'overview' | 'analysis'>('overview')

const analysisViewMode = ref<'chart' | 'table'>('chart')

const replayBuffer = ref<BacktestKlineAnalysis[]>([])
const replayIndex = ref(0)
const replayTotal = ref(0)
const replayPlaying = ref(false)
const replaySpeed = ref(1)
const tableBuffer = ref<BacktestKlineAnalysis[]>([])
const tableLoading = ref(false)
const tablePage = ref(1)
const tablePageSize = ref(10)
const tableActionFilter = ref('all')
const tableTotal = ref(0)
const tableTotalPages = ref(0)
let replayTimer: ReturnType<typeof setInterval> | null = null

const currentAnalysisItem = computed(() => {
  const buf = replayBuffer.value
  const idx = replayIndex.value
  if (buf.length === 0 || idx < 0 || idx >= buf.length) return null
  return buf[idx]
})

const tableDisplayItems = computed(() => tableBuffer.value)

const statusLabels: Record<string, string> = {
  Pending: '待处理',
  Running: '运行中',
  Completed: '已完成',
  Failed: '失败'
}
const statusColors: Record<string, string> = {
  Pending: '', Running: 'orange', Completed: 'green', Failed: 'red'
}

function formatDate(dt: string): string {
  if (!dt) return '-'
  return new Date(dt).toLocaleString('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit', second: '2-digit'
  })
}

/** Safe toFixed — treats null/undefined/NaN as 0 */
function fmt(v: number | null | undefined, decimals = 2): string {
  const n = Number(v)
  return (isFinite(n) ? n : 0).toFixed(decimals)
}

/** Normalize trade rows from the API.
 *  Handles legacy DB records where PnL was serialised as "pnL" (capital-L)
 *  instead of the corrected "pnl", and guards every numeric field.
 */
function fmtTrades(raw: unknown): Array<Record<string, unknown>> {
  if (!Array.isArray(raw)) return []
  return raw.map((t: Record<string, unknown>, idx: number) => {
    const pnl: number = Number(t.pnl ?? t.pnL ?? 0)
    const pnlPct: number = Number(t.pnlPercent ?? t.pnLPercent ?? 0)
    return {
      index: idx + 1,
      key: `${t.enteredAt ?? ''}-${t.exitedAt ?? ''}`,
      enteredAt: formatDate(t.enteredAt ?? ''),
      exitedAt: formatDate(t.exitedAt ?? ''),
      entryPrice: fmt(t.entryPrice),
      exitPrice: fmt(t.exitPrice),
      quantity: fmt(t.quantity, 4),
      pnl,
      pnlFormatted: (pnl >= 0 ? '+' : '') + pnl.toFixed(2),
      pnlPercentFormatted: (pnlPct >= 0 ? '+' : '') + pnlPct.toFixed(2) + '%',
    }
  })
}

function tradeRowClassName(record: Record<string, unknown>): string {
  const pnl = Number(record.pnl ?? 0)
  if (pnl > 0) return 'trade-row-up'
  if (pnl < 0) return 'trade-row-down'
  return ''
}

function load() {
  loading.value = true
  Promise.all([
    backtestsApi.getTask(taskId),
    backtestsApi.getResult(taskId).catch(() => ({ data: { result: null, status: '' } }))
  ]).then(([taskRes, resultRes]) => {
    task.value = taskRes.data
    result.value = resultRes.data.result
  }).catch(e => {
    error.value = e.response?.data?.message || '加载回测数据失败'
  }).finally(() => {
    loading.value = false
  })
}

function loadAnalysis() {
  backtestsApi.getAnalysis(taskId, 1, 100000).then(({ data }) => {
    replayBuffer.value = data.items ?? []
    replayTotal.value = data.total
    if (data.items && data.items.length > 0) {
      replayIndex.value = 0
    }
  })
}

function loadTable(page: number) {
  tablePage.value = Math.max(page, 1)
  tableLoading.value = true
  backtestsApi.getAnalysis(taskId, tablePage.value, tablePageSize.value, tableActionFilter.value).then(({ data }) => {
    tableBuffer.value = data.items ?? []
    tableTotal.value = data.total
    tableTotalPages.value = data.totalPages

    const maxPage = data.totalPages > 0 ? data.totalPages : 1
    if (tablePage.value > maxPage) {
      loadTable(maxPage)
      return
    }
  }).finally(() => {
    tableLoading.value = false
  })
}

function toggleTab(tab: 'overview' | 'analysis') {
  activeTab.value = tab
  if (tab === 'analysis' && replayBuffer.value.length === 0) {
    loadAnalysis()
  }
  if (tab === 'analysis' && tableBuffer.value.length === 0) {
    loadTable(1)
  }
}

function startReplay() {
  if (replayBuffer.value.length === 0) return
  replayPlaying.value = true
  if (replayIndex.value >= replayBuffer.value.length - 1) {
    replayIndex.value = 0
  }
  replayTimer = setInterval(() => {
    const step = replaySpeed.value
    const next = replayIndex.value + step
    if (next >= replayBuffer.value.length) {
      replayIndex.value = replayBuffer.value.length - 1
      stopReplay()
      return
    }
    replayIndex.value = next
  }, 50)
}

function stopReplay() {
  replayPlaying.value = false
  if (replayTimer) {
    clearInterval(replayTimer)
    replayTimer = null
  }
}

function onSeek(val: number) {
  replayIndex.value = Math.max(0, Math.min(val, replayBuffer.value.length - 1))
}

function onTableActionFilterChange(action: string) {
  tableActionFilter.value = action
  loadTable(1)
}

function onTablePageChange(page: number) {
  loadTable(page)
}

function onTablePageSizeChange(size: number) {
  tablePageSize.value = size
  loadTable(1)
}

onMounted(load)
onUnmounted(stopReplay)
</script>

<template>
  <div class="backtest-detail-page">
    <div v-if="loading" class="loading-state">加载中...</div>
    <div v-else-if="error" class="empty-state">{{ error }}</div>
    <template v-else-if="task">
      <div class="task-header">
        <div class="task-meta">
          <strong class="task-title">{{ task.strategyName || '回测任务' }}</strong>
          <span class="task-subtitle">
            {{ task.pair }} {{ task.timeframe }}
            &middot; {{ formatDate(task.startAt) }} ~ {{ formatDate(task.endAt) }}
          </span>
          <a-tag :color="statusColors[task.status] || ''">{{ statusLabels[task.status] || task.status }}</a-tag>
        </div>
      </div>

      <a-tabs :active-key="activeTab" @tab-click="(key: unknown) => toggleTab(key as 'overview' | 'analysis')">
        <a-tab-pane key="overview" title="概览">
          <template v-if="result">
          <div class="metrics-grid">
            <div class="metric-card">
              <span class="metric-value" :class="(result.totalReturnPercent ?? 0) >= 0 ? 'up' : 'down'">
                {{ (result.totalReturnPercent ?? 0) >= 0 ? '+' : '' }}{{ fmt(result.totalReturnPercent) }}%
              </span>
              <span class="metric-label">总收益率</span>
            </div>
            <div class="metric-card">
              <span class="metric-value" :class="(result.annualizedReturnPercent ?? 0) >= 0 ? 'up' : 'down'">
                {{ (result.annualizedReturnPercent ?? 0) >= 0 ? '+' : '' }}{{ fmt(result.annualizedReturnPercent) }}%
              </span>
              <span class="metric-label">年化收益率</span>
            </div>
            <div class="metric-card">
              <span class="metric-value down">{{ fmt(result.maxDrawdownPercent) }}%</span>
              <span class="metric-label">最大回撤</span>
            </div>
            <div class="metric-card">
              <span class="metric-value">{{ fmt(result.sharpeRatio) }}</span>
              <span class="metric-label">夏普比率</span>
            </div>
            <div class="metric-card">
              <span class="metric-value" :class="(result.winRate ?? 0) >= 50 ? 'up' : 'down'">{{ fmt(result.winRate, 1) }}%</span>
              <span class="metric-label">胜率</span>
            </div>
            <div class="metric-card">
              <span class="metric-value">{{ result.totalTrades ?? 0 }}</span>
              <span class="metric-label">交易次数</span>
            </div>
            <div class="metric-card">
              <span class="metric-value" :class="(result.profitLossRatio ?? 0) >= 1.5 ? 'up' : ((result.profitLossRatio ?? 0) >= 1 ? '' : 'down')">
                {{ fmt(result.profitLossRatio) }}
              </span>
              <span class="metric-label">盈亏比</span>
            </div>
          </div>

          <div v-if="Array.isArray(result.trades) && result.trades.length > 0" class="trades-section">
            <h4>交易记录 ({{ result.trades.length }})</h4>
            <a-table
              :columns="[
                { title: '#', dataIndex: 'index', width: 70 },
                { title: '入场', dataIndex: 'enteredAt', width: 170 },
                { title: '出场', dataIndex: 'exitedAt', width: 170 },
                { title: '入场价', dataIndex: 'entryPrice', width: 110 },
                { title: '出场价', dataIndex: 'exitPrice', width: 110 },
                { title: '数量', dataIndex: 'quantity', width: 110 },
                { title: '盈亏', dataIndex: 'pnlFormatted', width: 130 },
                { title: '收益率', dataIndex: 'pnlPercentFormatted', width: 120 },
              ]"
              :data="fmtTrades(result.trades)"
              :row-class="(record: Record<string, unknown>) => tradeRowClassName(record)"
              :pagination="false"
              stripe
              size="small"
            >
              <template #pnlFormatted="{ record }">
                <span :class="record.pnl >= 0 ? 'up' : 'down'">{{ record.pnlFormatted }}</span>
              </template>
              <template #pnlPercentFormatted="{ record }">
                <span :class="record.pnl >= 0 ? 'up' : 'down'">{{ record.pnlPercentFormatted }}</span>
              </template>
            </a-table>
          </div>
          </template>
          <div v-else class="result-pending">
            <a-result status="info" title="回测进行中">
              <template #subtitle>
                回测仍在运行，结果将在完成后生成。您可切换到「逐笔分析」Tab 查看已处理的部分 K 线数据。
              </template>
            </a-result>
          </div>
        </a-tab-pane>

        <a-tab-pane key="analysis" title="逐笔分析">
          <div class="analysis-toolbar">
            <div class="toolbar-left">
              <a-radio-group :model-value="analysisViewMode" type="button" @change="(v: unknown) => analysisViewMode = v as 'chart' | 'table'">
                <a-radio value="chart">图表</a-radio>
                <a-radio value="table">表格</a-radio>
              </a-radio-group>
            </div>
            <div v-if="analysisViewMode === 'chart'" class="toolbar-right">
              <a-button size="mini" :disabled="replayPlaying" @click="startReplay">
                <template #icon><icon-play-arrow-fill /></template>
                播放
              </a-button>
              <a-button size="mini" :disabled="!replayPlaying" @click="stopReplay">
                <template #icon><icon-pause-circle-fill /></template>
                暂停
              </a-button>
              <span class="speed-label">速度</span>
              <a-select size="mini" :model-value="replaySpeed" style="width: 70px" @change="(v: unknown) => replaySpeed = Number(v as number)">
                <a-option :value="1" label="1x" />
                <a-option :value="2" label="2x" />
                <a-option :value="5" label="5x" />
                <a-option :value="10" label="10x" />
              </a-select>
              <a-slider
                :min="0"
                :max="Math.max(0, replayBuffer.length - 1)"
                :model-value="replayIndex"
                :step="1"
                style="width: 160px"
                @change="(val: number | [number, number]) => { if (typeof val === 'number') onSeek(val) }"
              />
              <span class="replay-progress">K 线 {{ replayBuffer.length > 0 ? replayIndex + 1 : 0 }}/{{ replayBuffer.length }}</span>
            </div>
          </div>

          <div v-if="analysisViewMode === 'chart' && replayBuffer.length > 0" class="analysis-chart">
            <BacktestKlineAnalysisView
              :all-data="replayBuffer"
              :current-index="replayIndex"
              chart-only
            />
            <BacktestKlineCursorInfo
              :item="currentAnalysisItem"
              :total="replayTotal"
              :current="replayIndex"
            />
          </div>
          <div v-else-if="analysisViewMode === 'chart'" class="analysis-chart-placeholder">
            <a-result status="info" title="暂无 K 线数据">
              <template #subtitle>回测任务正在处理中，已处理的部分 K 线将在此展示。</template>
            </a-result>
          </div>

          <div v-else-if="analysisViewMode === 'table'" class="analysis-table">
            <div class="table-toolbar">
              <a-card class="condition-card" :bordered="false">
                <span class="condition-label">筛选条件</span>
                <a-select size="mini" :model-value="tableActionFilter" style="width: 120px" @change="(v: unknown) => onTableActionFilterChange(String(v))">
                  <a-option value="all" label="全部" />
                  <a-option value="enter" label="仅入场" />
                  <a-option value="exit" label="仅出场" />
                  <a-option value="none" label="无操作" />
                </a-select>
              </a-card>
            </div>
            <BacktestKlineAnalysisView :analysis="tableDisplayItems" table-only />
            <div class="table-pagination">
              <a-pagination
                :current="tablePage"
                :page-size="tablePageSize"
                :total="tableTotal"
                :show-total="true"
                :show-page-size="true"
                :page-size-options="[10, 20, 50, 100]"
                size="mini"
                @change="onTablePageChange"
                @page-size-change="onTablePageSizeChange"
              />
            </div>
          </div>
        </a-tab-pane>
      </a-tabs>
    </template>
  </div>
</template>

<style scoped>
.backtest-detail-page { padding: 0; }
.loading-state, .empty-state {
  text-align: center; color: var(--text-muted); padding: 3rem 1rem;
}
.task-header { display: flex; align-items: flex-start; gap: 1rem; margin-bottom: 1rem; }
.task-meta { display: flex; flex-direction: column; gap: 0.25rem; }
.task-title { font-size: 1.05rem; font-weight: 600; color: var(--text-primary); }
.task-subtitle { font-size: 0.8rem; color: var(--text-muted); }
.metrics-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 0.75rem; margin-bottom: 1.5rem; }
.metric-card { background: rgba(255,255,255,0.6); border: 1px solid var(--glass-border); border-radius: 8px; padding: 0.75rem 1rem; text-align: center; }
.metric-value { display: block; font-size: 1.4rem; font-weight: 700; letter-spacing: -0.03em; }
.metric-value.up { color: var(--accent-green); }
.metric-value.down { color: var(--accent-red); }
.metric-label { display: block; font-size: 0.75rem; color: var(--text-muted); margin-top: 0.15rem; }
.trades-section { margin-top: 1rem; }
.trades-section h4 { margin: 0 0 0.5rem; color: var(--text-primary); }
.trades-section :deep(.trade-row-up td) { color: var(--accent-green); }
.trades-section :deep(.trade-row-down td) { color: var(--accent-red); }
.analysis-toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem; gap: 0.5rem; }
.toolbar-left, .toolbar-right { display: flex; align-items: center; gap: 0.5rem; }
.speed-label { color: var(--text-muted); font-size: 0.8rem; }
.replay-progress { color: var(--text-muted); font-size: 0.8rem; white-space: nowrap; font-variant-numeric: tabular-nums; }
.analysis-chart { min-height: 400px; }
.analysis-chart-placeholder { min-height: 300px; display: flex; align-items: center; justify-content: center; }
.result-pending { min-height: 200px; display: flex; align-items: center; justify-content: center; }
.table-toolbar { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.5rem; }
.condition-card { margin: 0; }
.condition-card :deep(.arco-card-body) {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem 1rem;
}
.condition-label {
  font-size: 0.78rem;
  color: var(--text-muted);
  white-space: nowrap;
}
.table-pagination { display: flex; justify-content: flex-end; margin-top: 0.6rem; }

/* Replay scrubber */
.replay-scrubber {
  width: 100%;
  margin: 0.4rem 0;
  accent-color: var(--accent-blue, #4f7ec9);
  cursor: pointer;
}

</style>
