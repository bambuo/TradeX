<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { strategiesApi, type StrategyDeployment } from '../api/strategies'
import { backtestsApi, type BacktestTask, type BacktestResult } from '../api/backtests'

const route = useRoute()
const traderId = route.params.traderId as string
const strategyId = route.params.strategyId as string

const strategy = ref<StrategyDeployment | null>(null)
const tasks = ref<BacktestTask[]>([])
const loading = ref(true)
const running = ref(false)
const selectedResult = ref<BacktestResult | null>(null)

const days = ref(30)
const error = ref('')

onMounted(async () => {
  try {
    const { data } = await strategiesApi.getById(traderId, strategyId)
    strategy.value = data
    const { data: tasksData } = await backtestsApi.getTasks(traderId, strategyId)
    tasks.value = tasksData
  } catch {
    error.value = '加载策略信息失败'
  } finally {
    loading.value = false
  }
})

async function startBacktest() {
  if (!strategy.value) return
  running.value = true
  try {
    const endUtc = new Date().toISOString()
    const startUtc = new Date(Date.now() - days.value * 86400000).toISOString()
    const { data: task } = await backtestsApi.start(
      traderId, strategyId,
      strategy.value.exchangeId,
      strategy.value.symbolIds.split(',')[0] || '',
      strategy.value.timeframe,
      startUtc, endUtc
    )
    tasks.value.unshift({
      id: task.taskId,
      strategyId,
      status: task.status,
      startAtUtc: startUtc,
      endAtUtc: endUtc,
      createdAtUtc: task.createdAt,
      completedAtUtc: null
    })
    if (task.status === 'Completed') {
      const { data: result } = await backtestsApi.getResult(traderId, strategyId, task.taskId)
      selectedResult.value = result
    }
  } catch (e: any) {
    error.value = e.response?.data?.error || '回测启动失败'
  } finally {
    running.value = false
  }
}

async function selectTask(taskId: string) {
  try {
    const { data } = await backtestsApi.getResult(traderId, strategyId, taskId)
    selectedResult.value = data
  } catch {
    selectedResult.value = null
  }
}
</script>

<template>
  <div class="backtest-page">
    <div v-if="loading">加载中...</div>

    <template v-else-if="strategy">
      <div class="header">
        <h2>回测</h2>
        <span>策略: {{ strategy.strategyId }}</span>
      </div>

      <div class="config-card">
        <h3>配置</h3>
        <div class="config-row">
          <label>回测天数:</label>
          <select v-model.number="days">
            <option :value="7">7 天</option>
            <option :value="30">30 天</option>
            <option :value="60">60 天</option>
            <option :value="90">90 天</option>
            <option :value="180">180 天</option>
            <option :value="365">365 天</option>
          </select>
          <button class="btn-primary" :disabled="running" @click="startBacktest">
            {{ running ? '回测中...' : '开始回测' }}
          </button>
        </div>
        <div v-if="error" class="error">{{ error }}</div>
      </div>

      <div v-if="tasks.length" class="tasks-section">
        <h3>回测历史</h3>
        <div class="task-list">
          <div
            v-for="t in tasks"
            :key="t.id"
            class="task-item"
            :class="{ completed: t.status === 'Completed', running: t.status === 'Running' }"
            @click="selectTask(t.id)"
          >
            <span class="task-date">{{ new Date(t.createdAtUtc).toLocaleDateString() }}</span>
            <span class="task-status">{{ t.status }}</span>
          </div>
        </div>
      </div>

      <div v-if="selectedResult" class="result-card">
        <h3>回测结果</h3>
        <div class="metrics">
          <div class="metric">
            <span class="metric-label">总收益率</span>
            <span class="metric-value" :class="selectedResult.totalReturnPercent >= 0 ? 'positive' : 'negative'">
              {{ selectedResult.totalReturnPercent }}%
            </span>
          </div>
          <div class="metric">
            <span class="metric-label">年化收益率</span>
            <span class="metric-value">{{ selectedResult.annualizedReturnPercent }}%</span>
          </div>
          <div class="metric">
            <span class="metric-label">最大回撤</span>
            <span class="metric-value negative">{{ selectedResult.maxDrawdownPercent }}%</span>
          </div>
          <div class="metric">
            <span class="metric-label">胜率</span>
            <span class="metric-value">{{ selectedResult.winRate }}%</span>
          </div>
          <div class="metric">
            <span class="metric-label">总交易次数</span>
            <span class="metric-value">{{ selectedResult.totalTrades }}</span>
          </div>
          <div class="metric">
            <span class="metric-label">夏普比率</span>
            <span class="metric-value">{{ selectedResult.sharpeRatio }}</span>
          </div>
          <div class="metric">
            <span class="metric-label">盈亏比</span>
            <span class="metric-value">{{ selectedResult.profitLossRatio }}</span>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.backtest-page { padding: 2rem; }
.header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1rem; }
.header h2 { margin: 0; color: #e2e8f0; }
.config-card { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 1rem; margin-bottom: 1rem; }
.config-card h3 { margin: 0 0 0.75rem; color: #e2e8f0; }
.config-row { display: flex; align-items: center; gap: 1rem; }
.config-row select { padding: 0.5rem; background: #0f172a; color: #e2e8f0; border: 1px solid #334155; border-radius: 4px; }
.btn-primary { padding: 0.5rem 1rem; background: #38bdf8; color: #0f172a; border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.error { color: #ef4444; margin-top: 0.5rem; font-size: 0.85rem; }
.tasks-section { margin-bottom: 1rem; }
.tasks-section h3 { color: #e2e8f0; margin-bottom: 0.5rem; }
.task-list { display: flex; flex-direction: column; gap: 0.25rem; }
.task-item { display: flex; justify-content: space-between; padding: 0.5rem 0.75rem; background: #1e293b; border: 1px solid #334155; border-radius: 4px; cursor: pointer; color: #e2e8f0; font-size: 0.85rem; }
.task-item:hover { border-color: #38bdf8; }
.task-item.completed { border-left: 3px solid #22c55e; }
.task-item.running { border-left: 3px solid #f59e0b; }
.task-status { color: #94a3b8; }
.result-card { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 1rem; }
.result-card h3 { margin: 0 0 0.75rem; color: #e2e8f0; }
.metrics { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 1rem; }
.metric { display: flex; flex-direction: column; }
.metric-label { color: #94a3b8; font-size: 0.8rem; }
.metric-value { color: #e2e8f0; font-size: 1.1rem; font-weight: 600; }
.positive { color: #22c55e; }
.negative { color: #ef4444; }
</style>
