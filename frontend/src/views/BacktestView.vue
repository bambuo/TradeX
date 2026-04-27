<script setup lang="ts">
import { ref, computed, watch, onMounted, onUnmounted } from 'vue'
import { useRoute } from 'vue-router'
import { strategiesApi, type StrategyDeployment } from '../api/strategies'
import { backtestsApi, type BacktestTask, type BacktestResult, type BacktestCandleAnalysis } from '../api/backtests'
import BacktestCandleAnalysisView from '../components/BacktestCandleAnalysis.vue'
import AppSelect from '../components/AppSelect.vue'

const route = useRoute()
const traderId = route.params.traderId as string
const deploymentId = route.params.strategyId as string

const strategy = ref<StrategyDeployment | null>(null)
const tasks = ref<BacktestTask[]>([])
const loading = ref(true)
const running = ref(false)
const hasActiveTasks = ref(false)
const selectedTask = ref<BacktestTask | null>(null)
const selectedResult = ref<BacktestResult | null>(null)
const showDetail = ref(false)
const detailLoading = ref(false)
const activeTab = ref<'overview' | 'analysis'>('overview')

const analysisViewMode = ref<'chart' | 'table'>('chart')

const replayBuffer = ref<BacktestCandleAnalysis[]>([])
const replayIndex = ref(0)
const replayTotal = ref(0)
const replayPlaying = ref(false)
const replaySpeed = ref(1)
const tableBuffer = ref<BacktestCandleAnalysis[]>([])
const tableLoading = ref(false)
const tablePage = ref(1)
const tablePageSize = ref(100)

const days = ref(7)
const initialCapital = ref(1000)
const error = ref('')
let pollTimer: ReturnType<typeof setInterval> | null = null
let analysisAbort: AbortController | null = null
let replayTimer: ReturnType<typeof setInterval> | null = null
const analysisItems = computed(() =>
  replayBuffer.value.slice(0, replayIndex.value)
)
const displayAnalysisItems = computed(() =>
  enrichPositionMetrics(analysisItems.value, selectedTask.value?.initialCapital ?? 1000)
)
const analysisTotal = computed(() => replayTotal.value)
const currentAnalysisItem = computed(() => displayAnalysisItems.value.at(-1) ?? null)
const tableTotalPages = computed(() => Math.max(1, Math.ceil((analysisTotal.value || 0) / tablePageSize.value)))
const tableDisplayItems = computed(() => {
  const start = (tablePage.value - 1) * tablePageSize.value
  return enrichPositionMetrics(tableBuffer.value, selectedTask.value?.initialCapital ?? 1000).slice(start, start + tablePageSize.value)
})
const tableCurrentItem = computed(() => tableDisplayItems.value.at(-1) ?? null)

const statusLabels: Record<string, string> = {
  Pending: '排队中', Running: '运行中', Completed: '已完成', Failed: '失败', Cancelled: '已取消'
}
const statusColors: Record<string, string> = {
  Pending: 'var(--accent-amber)', Running: 'var(--accent-blue)', Completed: 'var(--accent-green)', Failed: 'var(--accent-red)', Cancelled: 'var(--text-muted)'
}
const phaseLabels: Record<string, string> = {
  Queued: '排队中', FetchingData: '获取数据', Running: '执行策略'
}

const speedOptions = [1, 2, 4, 8, 16]

onMounted(async () => {
  try {
    const { data } = await strategiesApi.getById(traderId, deploymentId)
    strategy.value = data
    await refreshTasks(data.strategyId)
  } catch {
    error.value = '加载策略信息失败'
  } finally {
    loading.value = false
  }
})

// Sync selectedTask with polling updates and re-trigger replay when status changes
watch(tasks, (newTasks) => {
  const old = selectedTask.value
  if (!old) return
  const updated = newTasks.find(t => t.id === old.id)
  if (!updated) return

  const oldStatus = old.status
  selectedTask.value = updated

  if (activeTab.value !== 'analysis' || analysisViewMode.value !== 'chart') return

  if (oldStatus === 'Pending' && updated.status === 'Running') {
    startReplay()
  } else if (oldStatus === 'Running' && updated.status === 'Completed') {
    stopReplay()
    startReplay()
  }
})

onUnmounted(() => { stopPolling(); stopReplay() })

function startPolling(templateId: string) {
  stopPolling()
  pollTimer = setInterval(async () => {
    await refreshTasks(templateId)
  }, 3000)
}

function stopPolling() {
  if (pollTimer) { clearInterval(pollTimer); pollTimer = null }
}

function closeAnalysisStream() {
  if (retryTimeout) { clearTimeout(retryTimeout); retryTimeout = null }
  if (analysisAbort) {
    analysisAbort.abort()
    analysisAbort = null
  }
}

async function refreshTasks(templateId: string) {
  try {
    const { data: tasksData } = await backtestsApi.getTasks(traderId, templateId)
    tasks.value = tasksData
    const active = tasksData.some(t => t.status === 'Pending' || t.status === 'Running')
    hasActiveTasks.value = active
    if (active) {
      startPolling(templateId)
    } else {
      stopPolling()
    }
  } catch {}
}

async function startBacktest() {
  if (!strategy.value) return
  running.value = true
  error.value = ''
  try {
    const endUtc = new Date().toISOString()
    const startUtc = new Date(Date.now() - days.value * 86400000).toISOString()
    const templateId = strategy.value.strategyId
    await backtestsApi.start(
      traderId, templateId, strategy.value.id,
      strategy.value.exchangeId,
      strategy.value.symbolIds.replace(/[\[\]"]/g, '').split(',')[0] || '',
      strategy.value.timeframe,
      startUtc, endUtc,
      initialCapital.value
    )
    await refreshTasks(templateId)
  } catch (e: any) {
    error.value = e.response?.data?.error || '回测启动失败'
  } finally {
    running.value = false
  }
}

async function openDetail(task: BacktestTask) {
  selectedTask.value = task
  selectedResult.value = null
  replayBuffer.value = []
  tableBuffer.value = []
  replayIndex.value = 0
  replayTotal.value = 0
  showDetail.value = true
  activeTab.value = 'overview'
  stopReplay()
  if (task.status !== 'Completed') return
  detailLoading.value = true
  try {
    const { data } = await backtestsApi.getResult(traderId, task.strategyId, task.id)
    selectedResult.value = data
  } catch {
    selectedResult.value = null
  } finally {
    detailLoading.value = false
  }
}

function switchTab(tab: 'overview' | 'analysis') {
  activeTab.value = tab
  if (tab === 'analysis' && selectedTask.value) {
    if (analysisViewMode.value === 'chart') {
      startReplay()
    } else {
      stopReplay()
      loadTableView()
    }
  } else {
    stopReplay()
  }
}

function startReplay() {
  stopReplay()
  const task = selectedTask.value
  if (!task) return

  if (task.status === 'Running') {
    openAnalysisStream()
    return
  }

  backtestsApi.getAnalysis(traderId, task.strategyId, task.id, 1, 100000).then(({ data }) => {
    replayBuffer.value = data.items ?? []
    replayTotal.value = data.total
    replayIndex.value = 0
    replayPlaying.value = true
    startReplayTimer()
  }).catch(() => {})
}

function startReplayTimer() {
  stopReplayTimer()
  const interval = 300 / replaySpeed.value
  if (interval < 10) {
    replayIndex.value = replayBuffer.value.length
    replayPlaying.value = false
    return
  }
  replayTimer = setInterval(() => {
    if (replayIndex.value >= replayBuffer.value.length) {
      replayPlaying.value = false
      stopReplayTimer()
      return
    }
    replayIndex.value++
  }, interval)
}

function stopReplayTimer() {
  if (replayTimer) { clearInterval(replayTimer); replayTimer = null }
}

function stopReplay() {
  stopReplayTimer()
  closeAnalysisStream()
}

function toggleReplay() {
  if (replayPlaying.value) {
    replayPlaying.value = false
    stopReplayTimer()
  } else {
    if (replayIndex.value >= replayBuffer.value.length) {
      replayIndex.value = 0
    }
    replayPlaying.value = true
    startReplayTimer()
  }
}

function restartReplay() {
  stopReplayTimer()
  replayIndex.value = 0
  replayPlaying.value = true
  startReplayTimer()
}

function changeSpeed(speed: number) {
  replaySpeed.value = speed
  if (replayPlaying.value) {
    startReplayTimer()
  }
}

function openAnalysisStream() {
  closeAnalysisStream()
  const task = selectedTask.value
  if (!task) return
  const url = `${window.location.origin}/api/traders/${traderId}/strategies/${task.strategyId}/backtests/tasks/${task.id}/analysis/stream`
  const token = localStorage.getItem('accessToken')
  analysisAbort = new AbortController()

  fetch(url, {
    signal: analysisAbort.signal,
    headers: token ? { Authorization: `Bearer ${token}` } : {}
  }).then(async (response) => {
    if (!response.ok) {
      scheduleStreamRetry()
      return
    }
    const reader = response.body?.getReader()
    if (!reader) {
      scheduleStreamRetry()
      return
    }
    const decoder = new TextDecoder()
    let buffer = ''
    let gotAnyData = false

    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      buffer += decoder.decode(value, { stream: true })
      const lines = buffer.split('\n')
      buffer = lines.pop() ?? ''

      for (const line of lines) {
        if (!line.startsWith('data: ')) continue
        try {
          const msg = JSON.parse(line.slice(6))
          if (msg.type === 'batch') {
            gotAnyData = true
            replayBuffer.value = msg.items ?? []
            replayTotal.value = msg.total ?? 0
            replayPlaying.value = true
            startReplayTimer()
          } else if (msg.type === 'item') {
            gotAnyData = true
            replayBuffer.value = [...replayBuffer.value, msg]
            replayTotal.value = replayBuffer.value.length
            if (!replayPlaying.value) {
              replayIndex.value = replayBuffer.value.length
            }
          }
        } catch {}
      }
    }

    // Stream ended — retry if no data and task still running
    if (!gotAnyData && selectedTask.value?.status === 'Running') {
      onReplayRetry()
    }
  }).catch(() => {})
}

let retryTimeout: ReturnType<typeof setTimeout> | null = null

function scheduleStreamRetry() {
  if (retryTimeout) return
  retryTimeout = setTimeout(() => {
    retryTimeout = null
    if (selectedTask.value?.status === 'Running') onReplayRetry()
  }, 3000)
}

function onReplayRetry() {
  retryTimeout = null
  if (analysisAbort?.signal.aborted) return
  if (!selectedTask.value || selectedTask.value.status !== 'Running') return
  openAnalysisStream()
}

function loadTableView() {
  if (!selectedTask.value) return
  tablePage.value = 1
  loadTablePage(1)
}

function loadTablePage(page: number) {
  if (!selectedTask.value) return
  tablePage.value = Math.min(Math.max(page, 1), tableTotalPages.value)
  const cumulativeSize = tablePage.value * tablePageSize.value
  tableLoading.value = true
  backtestsApi.getAnalysis(traderId, selectedTask.value.strategyId, selectedTask.value.id, 1, cumulativeSize).then(({ data }) => {
    tableBuffer.value = data.items ?? []
    replayTotal.value = data.total
  }).catch(() => {}).finally(() => {
    tableLoading.value = false
  })
}

function changeTablePageSize(size: number) {
  tablePageSize.value = size
  tablePage.value = 1
  loadTablePage(1)
}

function onViewModeChange(mode: 'chart' | 'table') {
  analysisViewMode.value = mode
  stopReplay()
  replayBuffer.value = []
  tableBuffer.value = []
  replayIndex.value = 0
  if (mode === 'chart') {
    startReplay()
  } else {
    loadTableView()
  }
}

function formatPercent(v: number): string {
  return v >= 0 ? `+${v.toFixed(2)}%` : `${v.toFixed(2)}%`
}

function formatCurrency(v: number | null | undefined): string {
  if (typeof v !== 'number' || Number.isNaN(v)) return '-'
  return `$${v.toLocaleString(undefined, { maximumFractionDigits: 2 })}`
}

function enrichPositionMetrics(items: BacktestCandleAnalysis[], initialValue: number): BacktestCandleAnalysis[] {
  const fallbackEntryValue = Math.min(100, initialValue)
  const legs: { quantity: number; cost: number }[] = []
  let realizedPnl = 0
  let lastQuantity = 0
  let lastCost = 0

  return items.map((item) => {
    if (item.positionCost !== null && item.positionCost !== undefined
      && item.positionQuantity !== null && item.positionQuantity !== undefined) {
      if (item.positionQuantity < lastQuantity) {
        const removedQuantity = lastQuantity - item.positionQuantity
        const avgCost = lastQuantity > 0 ? lastCost / lastQuantity : item.close
        const removedCost = avgCost * removedQuantity
        realizedPnl += item.close * removedQuantity - removedCost
      }

      const positionValue = item.positionQuantity * item.close
      const positionPnl = positionValue - item.positionCost
      const positionPnlPercent = item.positionCost > 0 ? (positionPnl / item.positionCost) * 100 : 0
      const taskPnl = realizedPnl + positionPnl
      const taskPnlPercent = initialValue > 0 ? (taskPnl / initialValue) * 100 : 0

      lastQuantity = item.positionQuantity
      lastCost = item.positionCost

      return {
        ...item,
        inPosition: item.inPosition || item.positionQuantity > 0,
        avgEntryPrice: item.positionQuantity > 0 ? item.positionCost / item.positionQuantity : item.avgEntryPrice,
        positionValue,
        positionPnl,
        positionPnlPercent,
        taskPnl,
        taskPnlPercent
      }
    }

    if (item.action === 'enter') {
      const cost = fallbackEntryValue
      const quantity = item.close > 0 ? cost / item.close : 0
      if (quantity > 0) legs.push({ quantity, cost })
    } else if (item.action === 'exit' && legs.length > 0) {
      const leg = legs.shift()!
      realizedPnl += item.close * leg.quantity - leg.cost
    }

    const positionQuantity = legs.reduce((sum, leg) => sum + leg.quantity, 0)
    const positionCost = legs.reduce((sum, leg) => sum + leg.cost, 0)
    const positionValue = positionQuantity > 0 ? positionQuantity * item.close : null
    const positionPnl = positionValue !== null ? positionValue - positionCost : null
    const positionPnlPercent = positionCost > 0 && positionPnl !== null ? (positionPnl / positionCost) * 100 : null
    const taskPnl = realizedPnl + (positionPnl ?? 0)
    const taskPnlPercent = initialValue > 0 ? (taskPnl / initialValue) * 100 : 0

    return {
      ...item,
      inPosition: item.inPosition || positionQuantity > 0,
      avgEntryPrice: positionQuantity > 0 ? positionCost / positionQuantity : null,
      positionQuantity: positionQuantity > 0 ? positionQuantity : null,
      positionCost: positionCost > 0 ? positionCost : null,
      positionValue,
      positionPnl,
      positionPnlPercent,
      taskPnl,
      taskPnlPercent
    }
  })
}
</script>

<template>
  <div class="backtest-page">
    <div v-if="loading">加载中...</div>

    <template v-else-if="strategy">
      <div class="header">
        <h2>回测</h2>
        <span class="strategy-name">{{ strategy.name || strategy.strategyId }}</span>
        <span v-if="strategy.scope" class="scope-tag">{{ strategy.scope }}</span>
      </div>

      <div class="config-card">
        <div class="config-row">
          <div class="config-field">
            <label>回测天数</label>
            <AppSelect
              :options="[
                { label: '7 天', value: 7 },
                { label: '30 天', value: 30 },
                { label: '60 天', value: 60 },
                { label: '90 天', value: 90 },
                { label: '180 天', value: 180 },
                { label: '365 天', value: 365 },
              ]"
              :model-value="days"
              form
              @update:model-value="(v: string | number) => days = Number(v)"
            />
          </div>
          <div class="config-field">
            <label>初始资金 ($)</label>
            <input v-model.number="initialCapital" type="number" min="100" step="100" />
          </div>
          <AppButton variant="primary" icon="chart" :disabled="running" @click="startBacktest">
            {{ running ? '提交中...' : '开始回测' }}
          </AppButton>
        </div>
        <div v-if="error" class="error-msg">{{ error }}</div>
      </div>

      <div v-if="tasks.length" class="tasks-section">
        <h3>回测任务</h3>
        <table class="task-table">
          <thead>
            <tr>
              <th>状态</th>
              <th>周期</th>
              <th>资金</th>
              <th>时间范围</th>
              <th>创建时间</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="t in tasks"
              :key="t.id"
              class="task-row"
              :class="{ 'task-row--running': t.status === 'Running' }"
              @click="openDetail(t)"
            >
              <td>
                <span class="status-dot" :style="{ background: statusColors[t.status] }"></span>
                {{ statusLabels[t.status] || t.status }}
                <span v-if="t.phase && t.status === 'Running'" class="phase-badge">{{ phaseLabels[t.phase] || t.phase }}</span>
                <span v-if="t.status === 'Running'" class="live-dot"></span>
              </td>
              <td>{{ t.symbolId || '-' }} {{ t.timeframe }}</td>
              <td>${{ t.initialCapital?.toLocaleString() ?? 1000 }}</td>
              <td class="date-cell">{{ new Date(t.startAtUtc).toLocaleDateString() }} ~ {{ new Date(t.endAtUtc).toLocaleDateString() }}</td>
              <td class="date-cell">{{ new Date(t.createdAt).toLocaleString() }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </template>

    <AppModal v-model="showDetail" title="回测详情" width="xl" @close="stopReplay">
      <template v-if="selectedTask">
        <div class="detail-summary">
          <span class="detail-status" :style="{ '--status-color': statusColors[selectedTask.status] }">
            {{ statusLabels[selectedTask.status] || selectedTask.status }}
          </span>
          <span>{{ selectedTask.strategyName || selectedTask.strategyId }}</span>
          <span>{{ selectedTask.symbolId }} {{ selectedTask.timeframe }}</span>
          <span>初始资金 ${{ selectedTask.initialCapital?.toLocaleString() ?? 1000 }}</span>
        </div>

        <div class="detail-meta">
          <div class="meta-item">
            <span class="meta-label">回测区间</span>
            <span class="meta-value">{{ new Date(selectedTask.startAtUtc).toLocaleDateString() }} ~ {{ new Date(selectedTask.endAtUtc).toLocaleDateString() }}</span>
          </div>
          <div class="meta-item">
            <span class="meta-label">创建时间</span>
            <span class="meta-value">{{ new Date(selectedTask.createdAt).toLocaleString() }}</span>
          </div>
          <div v-if="selectedTask.completedAtUtc" class="meta-item">
            <span class="meta-label">完成时间</span>
            <span class="meta-value">{{ new Date(selectedTask.completedAtUtc).toLocaleString() }}</span>
          </div>
        </div>

        <div v-if="detailLoading" class="loading-hint">加载结果中...</div>

        <div v-else-if="selectedTask.status === 'Failed'" class="error-msg">回测执行失败</div>

        <template v-else>
          <div class="tab-bar">
            <button
              class="tab-btn"
              :class="{ active: activeTab === 'overview' }"
              @click="switchTab('overview')"
            ><AppIcon name="chart" />概览</button>
            <button
              class="tab-btn"
              :class="{ active: activeTab === 'analysis' }"
              @click="switchTab('analysis')"
            ><AppIcon name="table" />K 线分析 ({{ selectedResult?.analysisCount ?? replayTotal ?? 0 }})</button>
          </div>

          <div v-if="activeTab === 'overview' && selectedResult" class="result-grid">
            <div class="result-item">
              <span class="result-label">总收益率</span>
              <span class="result-value" :class="selectedResult.totalReturnPercent >= 0 ? 'up' : 'down'">
                {{ formatPercent(selectedResult.totalReturnPercent) }}
              </span>
            </div>
            <div class="result-item">
              <span class="result-label">年化收益率</span>
              <span class="result-value" :class="selectedResult.annualizedReturnPercent >= 0 ? 'up' : 'down'">
                {{ formatPercent(selectedResult.annualizedReturnPercent) }}
              </span>
            </div>
            <div class="result-item">
              <span class="result-label">最大回撤</span>
              <span class="result-value down">{{ formatPercent(selectedResult.maxDrawdownPercent) }}</span>
            </div>
            <div class="result-item">
              <span class="result-label">胜率</span>
              <span class="result-value up">{{ selectedResult.winRate }}%</span>
            </div>
            <div class="result-item">
              <span class="result-label">总交易次数</span>
              <span class="result-value">{{ selectedResult.totalTrades }}</span>
            </div>
            <div class="result-item">
              <span class="result-label">夏普比率</span>
              <span class="result-value">{{ selectedResult.sharpeRatio.toFixed(2) }}</span>
            </div>
            <div class="result-item">
              <span class="result-label">盈亏比</span>
              <span class="result-value">{{ selectedResult.profitLossRatio.toFixed(2) }}</span>
            </div>
            <div class="result-item">
              <span class="result-label">理论最终资金</span>
              <span class="result-value up">
                ${{ ((selectedTask.initialCapital || 1000) * (1 + selectedResult.totalReturnPercent / 100)).toLocaleString(undefined, { maximumFractionDigits: 2 }) }}
              </span>
            </div>
          </div>
          <div v-else-if="activeTab === 'overview' && selectedTask.status === 'Running'" class="loading-hint">回测执行中，完成后将显示统计指标</div>

          <div v-if="activeTab === 'analysis' && analysisViewMode === 'chart'" class="replay-section">
            <div class="replay-bar">
              <AppButton variant="ghost" size="sm" icon="refresh" title="重新回放" @click="restartReplay" />
              <AppButton variant="primary" size="sm" :icon="replayPlaying ? 'pause' : (replayIndex >= (replayBuffer.length || 0) ? 'refresh' : 'play')" @click="toggleReplay" />
              <span class="replay-progress">
                {{ replayIndex }} / {{ replayTotal || '?' }}
              </span>
              <span class="replay-divider">|</span>
              <span class="replay-label">速度</span>
              <div class="speed-group">
                <button
                  v-for="s in speedOptions"
                  :key="s"
                  class="speed-btn"
                  :class="{ active: replaySpeed === s }"
                  @click="changeSpeed(s)"
                >{{ s }}x</button>
              </div>
              <span class="replay-divider">|</span>
              <AppButton size="sm" icon="table" @click="onViewModeChange('table')">表格</AppButton>
            </div>
            <div v-if="currentAnalysisItem" class="position-replay-card">
              <div class="position-stat">
                <span class="position-label">当前 K 线</span>
                <span class="position-value">{{ new Date(currentAnalysisItem.timestamp).toLocaleString() }}</span>
              </div>
              <div class="position-stat">
                <span class="position-label">持仓状态</span>
                <span class="position-value" :class="currentAnalysisItem.inPosition ? 'up' : ''">{{ currentAnalysisItem.inPosition ? '持仓中' : '空仓' }}</span>
              </div>
              <div class="position-stat">
                <span class="position-label">持仓均价</span>
                <span class="position-value">{{ formatCurrency(currentAnalysisItem.avgEntryPrice) }}</span>
              </div>
              <div class="position-stat">
                <span class="position-label">入场持仓价值</span>
                <span class="position-value">{{ formatCurrency(currentAnalysisItem.positionCost) }}</span>
              </div>
              <div class="position-stat">
                <span class="position-label">当前持仓价值</span>
                <span class="position-value">{{ formatCurrency(currentAnalysisItem.positionValue) }}</span>
              </div>
              <div class="position-stat">
                <span class="position-label">盈亏金额</span>
                <span class="position-value" :class="(currentAnalysisItem.taskPnl ?? 0) >= 0 ? 'up' : 'down'">
                  {{ formatCurrency(currentAnalysisItem.taskPnl) }}
                </span>
              </div>
              <div class="position-stat">
                <span class="position-label">盈亏比</span>
                <span class="position-value" :class="(currentAnalysisItem.taskPnlPercent ?? 0) >= 0 ? 'up' : 'down'">
                  {{ currentAnalysisItem.taskPnlPercent !== null && currentAnalysisItem.taskPnlPercent !== undefined ? formatPercent(currentAnalysisItem.taskPnlPercent) : '-' }}
                </span>
              </div>
            </div>
            <BacktestCandleAnalysisView
              v-if="displayAnalysisItems.length > 0"
              :analysis="displayAnalysisItems"
              chart-only
            />
            <div v-else class="loading-hint">
              {{ selectedTask?.status === 'Running' ? '等待回测数据...' : '加载中...' }}
            </div>
          </div>

          <div v-if="activeTab === 'analysis' && analysisViewMode === 'table'" class="analysis-table-section">
            <div class="table-bar">
              <span class="table-label">共 {{ analysisTotal }} 根 K 线</span>
              <div class="pagination-controls">
                <span class="table-label">每页</span>
                <AppSelect
                  :options="[
                    { label: '50', value: 50 },
                    { label: '100', value: 100 },
                    { label: '200', value: 200 },
                    { label: '500', value: 500 }
                  ]"
                  :model-value="tablePageSize"
                  @update:model-value="(v: string | number) => changeTablePageSize(Number(v))"
                />
                <AppButton size="sm" variant="ghost" :disabled="tablePage <= 1" @click="loadTablePage(tablePage - 1)">上一页</AppButton>
                <span class="page-label">{{ tablePage }} / {{ tableTotalPages }}</span>
                <AppButton size="sm" variant="ghost" :disabled="tablePage >= tableTotalPages" @click="loadTablePage(tablePage + 1)">下一页</AppButton>
              </div>
              <AppButton size="sm" icon="chart" @click="onViewModeChange('chart')">K 线图</AppButton>
            </div>
            <div v-if="currentAnalysisItem" class="position-replay-card compact">
              <div class="position-stat">
                <span class="position-label">入场持仓价值</span>
                <span class="position-value">{{ formatCurrency(tableDisplayItems.at(-1)?.positionCost) }}</span>
              </div>
              <div class="position-stat">
                <span class="position-label">当前持仓价值</span>
                <span class="position-value">{{ formatCurrency(tableDisplayItems.at(-1)?.positionValue) }}</span>
              </div>
              <div class="position-stat">
                <span class="position-label">盈亏金额</span>
                <span class="position-value" :class="(tableDisplayItems.at(-1)?.taskPnl ?? 0) >= 0 ? 'up' : 'down'">
                  {{ formatCurrency(tableDisplayItems.at(-1)?.taskPnl) }}
                </span>
              </div>
              <div class="position-stat">
                <span class="position-label">盈亏比</span>
                <span class="position-value" :class="(tableDisplayItems.at(-1)?.taskPnlPercent ?? 0) >= 0 ? 'up' : 'down'">
                  {{ tableDisplayItems.at(-1)?.taskPnlPercent !== null && tableDisplayItems.at(-1)?.taskPnlPercent !== undefined ? formatPercent(tableDisplayItems.at(-1)!.taskPnlPercent!) : '-' }}
                </span>
              </div>
            </div>
            <div v-if="tableLoading" class="loading-hint">加载当前页...</div>
            <div v-else-if="tableDisplayItems.length > 0">
              <BacktestCandleAnalysisView :analysis="tableDisplayItems" table-only />
            </div>
            <div v-else class="loading-hint">没有可用的 K 线分析数据</div>
          </div>
        </template>

      </template>

      <template #footer>
        <AppButton variant="primary" icon="close" @click="showDetail = false; stopReplay()">关闭</AppButton>
      </template>
    </AppModal>
  </div>
</template>

<style scoped>
.backtest-page { padding: 2rem; }
.header { display: flex; align-items: center; gap: 0.75rem; margin-bottom: 1rem; }
.header h2 { margin: 0; color: var(--text-primary); }
.strategy-name { color: var(--text-muted); font-size: 0.9rem; }
.scope-tag { padding: 0.125rem 0.375rem; border-radius: 4px; background: rgba(56,189,248,0.1); color: var(--accent-blue); font-size: 0.7rem; }

.config-card { background: rgba(255,255,255,0.55); border: 1px solid var(--glass-border); border-radius: 6px; padding: 1rem; margin-bottom: 1rem; }
.config-row { display: flex; align-items: flex-end; gap: 1rem; flex-wrap: wrap; }
.config-field { display: flex; flex-direction: column; gap: 0.25rem; }
.config-field label { color: var(--text-muted); font-size: 0.8rem; }
.config-field select, .config-field input {
  padding: 0.5rem; background: rgba(255,255,255,0.35); color: var(--text-primary);
  border: 1px solid var(--glass-border); border-radius: 4px; width: 120px;
}
.config-field input { width: 130px; }
.btn-primary { padding: 0.5rem 1rem; background: var(--accent-blue); color: var(--text-primary); border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.error-msg { color: var(--accent-red); margin-top: 0.5rem; font-size: 0.85rem; }

.tasks-section { margin-bottom: 1rem; }
.tasks-section h3 { color: var(--text-primary); margin-bottom: 0.5rem; }
.task-table { width: 100%; border-collapse: collapse; }
.task-table th, .task-table td { padding: 0.5rem 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); font-size: 0.85rem; }
.task-table th { color: var(--text-muted); font-weight: 600; }
.task-row { cursor: pointer; transition: background 0.1s; }
.task-row:hover { background: rgba(79, 126, 201, 0.04); }
.task-row--running { background: rgba(79, 126, 201, 0.06); }
.task-row--running:hover { background: rgba(79, 126, 201, 0.1); }
.status-dot { display: inline-block; width: 8px; height: 8px; border-radius: 50%; margin-right: 0.375rem; vertical-align: middle; }
.phase-badge {
  display: inline-block; margin-left: 0.375rem; padding: 0.0625rem 0.375rem;
  border-radius: 999px; background: rgba(79, 126, 201, 0.15);
  color: var(--accent-blue); font-size: 0.7rem; font-weight: 500; vertical-align: middle;
}
.live-dot {
  display: inline-block; width: 6px; height: 6px; border-radius: 50%;
  background: var(--accent-green); margin-left: 0.375rem; vertical-align: middle;
  animation: pulse-dot 1.5s ease-in-out infinite;
}
@keyframes pulse-dot {
  0%, 100% { opacity: 1; transform: scale(1); }
  50% { opacity: 0.3; transform: scale(0.7); }
}
.date-cell { color: var(--text-muted); }

.tab-bar { display: flex; gap: 0; margin-bottom: 1rem; border-bottom: 1px solid var(--glass-border); }
.tab-btn {
  padding: 0.5rem 1rem; background: transparent; color: var(--text-muted);
  border: none; border-bottom: 2px solid transparent; cursor: pointer;
  font-size: 0.85rem; transition: all 0.15s;
}
.tab-btn:hover { color: var(--text-primary); }
.tab-btn.active { color: var(--accent-blue); border-bottom-color: var(--accent-blue); }
.detail-summary { display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap; margin-bottom: 1rem; padding: 0.75rem 1rem; background: rgba(255,255,255,0.55); border: 1px solid var(--glass-border); border-radius: 6px; font-size: 0.85rem; color: var(--text-muted); }
.detail-status { display: inline-block; padding: 0.125rem 0.5rem; border-radius: 999px; color: var(--status-color); font-size: 0.8rem; font-weight: 600; background: color-mix(in srgb, var(--status-color) 12%, transparent); border: 1px solid color-mix(in srgb, var(--status-color) 25%, transparent); }
.detail-meta { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; margin-bottom: 1rem; padding: 0.75rem 1rem; background: rgba(255,255,255,0.55); border: 1px solid var(--glass-border); border-radius: 6px; }
.meta-item { display: flex; flex-direction: column; gap: 0.125rem; }
.meta-label { color: var(--text-muted); font-size: 0.75rem; }
.meta-value { color: var(--text-primary); font-size: 0.85rem; }
.loading-hint { color: var(--text-muted); font-size: 0.85rem; padding: 1rem 0; text-align: center; }
.result-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; margin-bottom: 1rem; }
.result-item { display: flex; flex-direction: column; gap: 0.125rem; padding: 0.75rem 1rem; background: rgba(255,255,255,0.45); border: 1px solid var(--glass-border); border-radius: 6px; }
.result-label { color: var(--text-muted); font-size: 0.75rem; }
.result-value { color: var(--text-primary); font-size: 1rem; font-weight: 600; }
.result-value.up { color: var(--accent-green); }
.result-value.down { color: var(--accent-red); }
.replay-section { margin-top: 0.5rem; }
.replay-bar {
  display: flex; align-items: center; gap: 0.5rem;
  padding: 0.5rem; background: rgba(255,255,255,0.35); border: 1px solid var(--glass-border);
  border-radius: 6px; margin-bottom: 0.5rem; flex-wrap: wrap;
}
.replay-btn {
  padding: 0.25rem 0.5rem; background: transparent; color: var(--text-muted);
  border: 1px solid var(--glass-border); border-radius: 4px; cursor: pointer;
  font-size: 0.85rem; line-height: 1; transition: all 0.15s;
}
.replay-btn:hover { color: var(--text-primary); border-color: var(--text-muted); }
.replay-btn.primary { color: var(--accent-blue); font-size: 1rem; padding: 0.25rem 0.625rem; }
.replay-btn.primary:hover { background: rgba(79,126,201,0.1); }
.replay-progress { color: var(--text-muted); font-size: 0.8rem; font-family: 'SF Mono', monospace; min-width: 80px; }
.replay-divider { color: var(--glass-border-strong); font-size: 0.8rem; }
.replay-label { color: var(--text-muted); font-size: 0.75rem; }
.speed-group { display: flex; gap: 2px; }
.speed-btn {
  padding: 0.125rem 0.375rem; background: rgba(255,255,255,0.55); color: var(--text-muted);
  border: 1px solid transparent; border-radius: 3px; cursor: pointer;
  font-size: 0.75rem; transition: all 0.15s;
}
.speed-btn:hover { color: var(--text-primary); }
.speed-btn.active { background: rgba(79,126,201,0.15); color: var(--accent-blue); border-color: var(--accent-blue); }
.position-replay-card {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.75rem;
  padding: 0.75rem;
  margin-bottom: 0.5rem;
  background: rgba(255,255,255,0.45);
  border: 1px solid var(--glass-border);
  border-radius: 6px;
}
.position-replay-card.compact { grid-template-columns: repeat(3, minmax(0, 1fr)); }
.position-stat { display: flex; flex-direction: column; gap: 0.125rem; min-width: 0; }
.position-label { color: var(--text-muted); font-size: 0.72rem; }
.position-value { color: var(--text-primary); font-size: 0.9rem; font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.position-value.up { color: var(--accent-green); }
.position-value.down { color: var(--accent-red); }
.table-bar {
  display: flex; align-items: center; justify-content: space-between;
  padding: 0.5rem; background: rgba(255,255,255,0.35); border: 1px solid var(--glass-border);
  border-radius: 6px; margin-bottom: 0.5rem; gap: 0.75rem; flex-wrap: wrap;
}
.table-label { color: var(--text-muted); font-size: 0.8rem; }
.pagination-controls { display: flex; align-items: center; gap: 0.5rem; flex-wrap: wrap; }
.page-label { color: var(--text-primary); font-size: 0.8rem; font-family: 'SF Mono', monospace; min-width: 64px; text-align: center; }

.load-more-wrap { text-align: center; padding: 0.75rem 0; }
.load-more-btn {
  padding: 0.375rem 1rem; background: rgba(255,255,255,0.55); color: var(--accent-blue);
  border: 1px solid var(--glass-border); border-radius: 4px; cursor: pointer; font-size: 0.8rem;
}
.load-more-btn:hover { background: rgba(56,189,248,0.1); }
.load-more-btn:disabled { opacity: 0.5; cursor: not-allowed; }
</style>
