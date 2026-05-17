<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { backtestsApi, type BacktestTask, type BacktestResult, type BacktestKlineAnalysis } from '../api/backtests'
import BacktestKlineAnalysisView from '../components/BacktestKlineAnalysis.vue'

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

const analysisItems = computed(() =>
  replayBuffer.value.slice(0, replayIndex.value)
)
const displayAnalysisItems = computed(() =>
  analysisItems.value.filter((_, i) => i % replaySpeed.value === 0)
)

const tableDisplayItems = computed(() => {
  const data = tableBuffer.value
  const actions = tableActionFilter.value
  if (actions === 'all') return data
  return data.filter(d => d.action === actions)
})

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

function load() {
  loading.value = true
  Promise.all([
    backtestsApi.getTask(taskId),
    backtestsApi.getResult(taskId)
  ]).then(([taskRes, resultRes]) => {
    task.value = taskRes.data
    result.value = resultRes.data
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
  })
}

function loadTable(page: number) {
  tablePage.value = Math.min(Math.max(page, 1), tableTotalPages.value)
  tableLoading.value = true
  backtestsApi.getAnalysis(taskId, tablePage.value, tablePageSize.value, tableActionFilter.value).then(({ data }) => {
    tableBuffer.value = data.items ?? []
    replayTotal.value = data.total
    tableTotal.value = data.total
    tableTotalPages.value = data.totalPages
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
  replayPlaying.value = true
  replayIndex.value = 0
  replayTimer = setInterval(() => {
    if (replayIndex.value >= replayBuffer.value.length) {
      stopReplay()
      return
    }
    replayIndex.value++
  }, 100)
}

function stopReplay() {
  replayPlaying.value = false
  if (replayTimer) {
    clearInterval(replayTimer)
    replayTimer = null
  }
}

function onTableActionFilterChange(action: string) {
  tableActionFilter.value = action
  loadTable(1)
}

onMounted(load)
</script>

<template>
  <div class="backtest-detail-page">
    <div v-if="loading" class="loading-state">加载中...</div>
    <div v-else-if="error" class="empty-state">{{ error }}</div>
    <template v-else-if="task && result">
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
          <div class="metrics-grid">
            <div class="metric-card">
              <span class="metric-value" :class="result.totalReturnPercent >= 0 ? 'up' : 'down'">
                {{ result.totalReturnPercent >= 0 ? '+' : '' }}{{ result.totalReturnPercent.toFixed(2) }}%
              </span>
              <span class="metric-label">总收益率</span>
            </div>
            <div class="metric-card">
              <span class="metric-value" :class="result.annualizedReturnPercent >= 0 ? 'up' : 'down'">
                {{ result.annualizedReturnPercent >= 0 ? '+' : '' }}{{ result.annualizedReturnPercent.toFixed(2) }}%
              </span>
              <span class="metric-label">年化收益率</span>
            </div>
            <div class="metric-card">
              <span class="metric-value down">{{ result.maxDrawdownPercent.toFixed(2) }}%</span>
              <span class="metric-label">最大回撤</span>
            </div>
            <div class="metric-card">
              <span class="metric-value">{{ result.sharpeRatio.toFixed(2) }}</span>
              <span class="metric-label">夏普比率</span>
            </div>
            <div class="metric-card">
              <span class="metric-value" :class="result.winRate >= 50 ? 'up' : 'down'">{{ result.winRate.toFixed(1) }}%</span>
              <span class="metric-label">胜率</span>
            </div>
            <div class="metric-card">
              <span class="metric-value">{{ result.totalTrades }}</span>
              <span class="metric-label">交易次数</span>
            </div>
            <div class="metric-card">
              <span class="metric-value" :class="result.profitLossRatio >= 1.5 ? 'up' : (result.profitLossRatio >= 1 ? '' : 'down')">
                {{ result.profitLossRatio.toFixed(2) }}
              </span>
              <span class="metric-label">盈亏比</span>
            </div>
          </div>

          <div v-if="result.trades.length > 0" class="trades-section">
            <h4>交易记录 ({{ result.trades.length }})</h4>
            <a-table
              :columns="[
                { title: '入场', dataIndex: 'enteredAt', width: 160 },
                { title: '出场', dataIndex: 'exitedAt', width: 160 },
                { title: '入场价', dataIndex: 'entryPrice' },
                { title: '出场价', dataIndex: 'exitPrice' },
                { title: '数量', dataIndex: 'quantity' },
                { title: '盈亏', dataIndex: 'pnlFormatted' },
                { title: '收益率', dataIndex: 'pnlPercentFormatted' },
              ]"
              :data="result.trades.map(t => ({
                ...t,
                key: t.enteredAt + '-' + t.exitedAt,
                enteredAt: formatDate(t.enteredAt),
                exitedAt: formatDate(t.exitedAt),
                entryPrice: t.entryPrice.toFixed(2),
                exitPrice: t.exitPrice.toFixed(2),
                quantity: t.quantity.toFixed(4),
                pnlFormatted: t.pnl >= 0 ? '+'+t.pnl.toFixed(2) : t.pnl.toFixed(2),
                pnlPercentFormatted: (t.pnlPercent >= 0 ? '+' : '') + t.pnlPercent.toFixed(2) + '%'
              }))"
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
              <a-select :model-value="replaySpeed" style="width: 70px" @change="(v: unknown) => replaySpeed = Number(v as number)">
                <a-option :value="1" label="1x" />
                <a-option :value="2" label="2x" />
                <a-option :value="5" label="5x" />
                <a-option :value="10" label="10x" />
              </a-select>
              <span class="replay-progress">K 线 {{ replayIndex }}/{{ replayBuffer.length }}</span>
            </div>
          </div>

          <div v-if="analysisViewMode === 'chart' && replayBuffer.length > 0" class="analysis-chart">
            <BacktestKlineAnalysisView :analysis="displayAnalysisItems" chart-only />
          </div>

          <div v-else-if="analysisViewMode === 'table'" class="analysis-table">
            <div class="table-toolbar">
              <a-select :model-value="tableActionFilter" style="width: 120px" @change="(v: unknown) => onTableActionFilterChange(String(v))">
                <a-option value="all" label="全部" />
                <a-option value="enter" label="仅入场" />
                <a-option value="exit" label="仅出场" />
                <a-option value="none" label="无操作" />
              </a-select>
              <span>共 {{ tableTotal }} 条</span>
            </div>
            <BacktestKlineAnalysisView :analysis="tableDisplayItems" table-only />
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
.analysis-toolbar { display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.75rem; gap: 0.5rem; }
.toolbar-left, .toolbar-right { display: flex; align-items: center; gap: 0.5rem; }
.speed-label { color: var(--text-muted); font-size: 0.8rem; }
.replay-progress { color: var(--text-muted); font-size: 0.8rem; white-space: nowrap; }
.analysis-chart { min-height: 400px; }
.table-toolbar { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.5rem; }
</style>
