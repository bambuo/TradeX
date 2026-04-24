<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { strategiesApi, type Strategy } from '../api/strategies'
import { backtestsApi, type BacktestTask, type BacktestResult } from '../api/backtests'
import BacktestChart from '../components/BacktestChart.vue'

const route = useRoute()
const router = useRouter()
const traderId = route.params.traderId as string
const strategyId = route.params.strategyId as string

const strategy = ref<Strategy | null>(null)
const tasks = ref<BacktestTask[]>([])
const loading = ref(true)
const running = ref(false)
const selectedResult = ref<BacktestResult | null>(null)
const selectedTaskId = ref<string | null>(null)

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
  error.value = ''
  running.value = true
  try {
    const endUtc = new Date().toISOString()
    const startUtc = new Date(Date.now() - days.value * 86400000).toISOString()
    const { data } = await backtestsApi.start(traderId, strategyId, startUtc, endUtc)
    const { data: tasksData } = await backtestsApi.getTasks(traderId, strategyId)
    tasks.value = tasksData
    selectedTaskId.value = data.taskId
    await loadResult(data.taskId)
  } catch (err: any) {
    error.value = err?.response?.data?.error || '启动回测失败'
  } finally {
    running.value = false
  }
}

async function loadResult(taskId: string) {
  selectedTaskId.value = taskId
  selectedResult.value = null
  try {
    const { data } = await backtestsApi.getResult(traderId, strategyId, taskId)
    selectedResult.value = data
  } catch {
    // not ready yet
  }
}

function formatPercent(v: number) {
  return `${v >= 0 ? '+' : ''}${v.toFixed(2)}%`
}

function formatNumber(v: number) {
  return v.toLocaleString(undefined, { maximumFractionDigits: 2 })
}
</script>

<template>
  <div class="backtest-page">
    <button class="btn-back" @click="router.push(`/traders/${traderId}/strategies`)">← 策略列表</button>

    <div v-if="loading">加载中...</div>

    <template v-else-if="strategy">
      <div class="header">
        <h2>回测: {{ strategy.name }}</h2>
        <span class="badge" :class="strategy.status.toLowerCase()">{{ strategy.status }}</span>
      </div>

      <div class="config-card">
        <h3>配置</h3>
        <div class="config-row">
          <label>K 线周期: <strong>{{ strategy.timeframe }}</strong></label>
          <label>交易对: <strong>{{ strategy.symbolIds }}</strong></label>
        </div>
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

      <div v-if="tasks.length > 0" class="tasks-section">
        <h3>历史回测</h3>
        <table class="table">
          <thead>
            <tr>
              <th>开始时间</th>
              <th>结束时间</th>
              <th>状态</th>
              <th>完成时间</th>
              <th>操作</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="t in tasks" :key="t.id">
              <td>{{ new Date(t.startAtUtc).toLocaleDateString() }}</td>
              <td>{{ new Date(t.endAtUtc).toLocaleDateString() }}</td>
              <td>
                <span class="status-badge" :class="t.status.toLowerCase()">{{ t.status }}</span>
              </td>
              <td>{{ t.completedAtUtc ? new Date(t.completedAtUtc).toLocaleString() : '-' }}</td>
              <td>
                <button v-if="t.status === 'Completed'" class="btn-small" @click="loadResult(t.id)">查看结果</button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <div v-if="selectedResult" class="result-card">
        <h3>回测结果</h3>
        <div class="metrics-grid">
          <div class="metric">
            <span class="metric-value" :class="selectedResult.totalReturnPercent >= 0 ? 'positive' : 'negative'">
              {{ formatPercent(selectedResult.totalReturnPercent) }}
            </span>
            <span class="metric-label">总收益率</span>
          </div>
          <div class="metric">
            <span class="metric-value" :class="selectedResult.annualizedReturnPercent >= 0 ? 'positive' : 'negative'">
              {{ formatPercent(selectedResult.annualizedReturnPercent) }}
            </span>
            <span class="metric-label">年化收益率</span>
          </div>
          <div class="metric">
            <span class="metric-value negative">{{ formatPercent(selectedResult.maxDrawdownPercent) }}</span>
            <span class="metric-label">最大回撤</span>
          </div>
          <div class="metric">
            <span class="metric-value">{{ formatPercent(selectedResult.winRate) }}</span>
            <span class="metric-label">胜率</span>
          </div>
          <div class="metric">
            <span class="metric-value">{{ selectedResult.totalTrades }}</span>
            <span class="metric-label">总交易次数</span>
          </div>
          <div class="metric">
            <span class="metric-value">{{ formatNumber(selectedResult.sharpeRatio) }}</span>
            <span class="metric-label">夏普比率</span>
          </div>
          <div class="metric">
            <span class="metric-value">{{ formatNumber(selectedResult.profitLossRatio) }}</span>
            <span class="metric-label">盈亏比</span>
          </div>
        </div>

        <BacktestChart v-if="selectedResult.trades && selectedResult.trades.length > 0" :trades="selectedResult.trades" />

        <div v-if="selectedResult.trades && selectedResult.trades.length > 0" class="trades-section">
          <h4>交易明细 ({{ selectedResult.trades.length }})</h4>
          <table class="table">
            <thead>
              <tr>
                <th>入场时间</th>
                <th>出场时间</th>
                <th>入场价</th>
                <th>出场价</th>
                <th>数量</th>
                <th>盈亏</th>
                <th>收益率</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="(trade, i) in selectedResult.trades" :key="i">
                <td>{{ new Date(trade.entryTime).toLocaleString() }}</td>
                <td>{{ new Date(trade.exitTime).toLocaleString() }}</td>
                <td>{{ formatNumber(trade.entryPrice) }}</td>
                <td>{{ formatNumber(trade.exitPrice) }}</td>
                <td>{{ formatNumber(trade.quantity) }}</td>
                <td :class="trade.pnl >= 0 ? 'positive' : 'negative'">{{ formatNumber(trade.pnl) }}</td>
                <td :class="trade.pnlPercent >= 0 ? 'positive' : 'negative'">{{ formatPercent(trade.pnlPercent) }}</td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </template>
  </div>
</template>

<style scoped>
.backtest-page { padding: 2rem; }
.btn-back { padding: 0.5rem 1rem; background: transparent; color: #94a3b8; border: 1px solid #334155; border-radius: 4px; cursor: pointer; margin-bottom: 1rem; }
.btn-back:hover { color: #e2e8f0; }
.header { display: flex; align-items: center; gap: 1rem; margin-bottom: 1.5rem; }
.header h2 { margin: 0; color: #e2e8f0; }
.badge { padding: 0.25rem 0.75rem; border-radius: 12px; font-size: 0.8rem; font-weight: 600; }
.badge.active { background: rgba(56,189,248,0.15); color: #38bdf8; }
.badge.draft { background: rgba(148,163,184,0.15); color: #94a3b8; }
.badge.backtesting { background: rgba(245,158,11,0.15); color: #f59e0b; }
.badge.passed { background: rgba(34,197,94,0.15); color: #22c55e; }
.badge.disabled { background: rgba(239,68,68,0.15); color: #ef4444; }

.config-card { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 1.5rem; margin-bottom: 1.5rem; }
.config-card h3 { color: #e2e8f0; margin: 0 0 1rem; }
.config-row { display: flex; align-items: center; gap: 1rem; margin-bottom: 0.75rem; flex-wrap: wrap; }
.config-row label { color: #94a3b8; font-size: 0.9rem; }
.config-row strong { color: #e2e8f0; }
.config-row select { padding: 0.5rem; background: #0f172a; color: #e2e8f0; border: 1px solid #334155; border-radius: 4px; }
.btn-primary { padding: 0.5rem 1.5rem; background: #38bdf8; color: #0f172a; border: none; border-radius: 4px; font-weight: 600; cursor: pointer; }
.btn-primary:disabled { opacity: 0.5; }
.error { color: #ef4444; font-size: 0.9rem; margin-top: 0.5rem; }

.table { width: 100%; border-collapse: collapse; margin-top: 0.5rem; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; font-size: 0.85rem; }
.table th { color: #94a3b8; font-weight: 600; }
.btn-small { padding: 0.25rem 0.75rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
.btn-small:hover { background: #475569; }
.status-badge { padding: 0.15rem 0.5rem; border-radius: 8px; font-size: 0.8rem; }
.status-badge.completed { background: rgba(34,197,94,0.15); color: #22c55e; }
.status-badge.failed { background: rgba(239,68,68,0.15); color: #ef4444; }
.status-badge.pending { background: rgba(148,163,184,0.15); color: #94a3b8; }
.status-badge.running { background: rgba(245,158,11,0.15); color: #f59e0b; }

.tasks-section { margin-bottom: 1.5rem; }
.tasks-section h3 { color: #e2e8f0; margin: 0 0 0.5rem; }

.result-card { background: #1e293b; border: 1px solid #334155; border-radius: 8px; padding: 1.5rem; }
.result-card h3 { color: #e2e8f0; margin: 0 0 1rem; }
.metrics-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 1rem; margin-bottom: 1.5rem; }
.metric { display: flex; flex-direction: column; align-items: center; gap: 0.25rem; }
.metric-value { font-size: 1.5rem; font-weight: 700; }
.metric-label { font-size: 0.8rem; color: #64748b; }
.positive { color: #22c55e; }
.negative { color: #ef4444; }
.trades-section { margin-top: 1rem; }
.trades-section h4 { color: #e2e8f0; margin: 0 0 0.5rem; font-size: 0.95rem; }
</style>
