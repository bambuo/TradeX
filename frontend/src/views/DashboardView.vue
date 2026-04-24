<script setup lang="ts">
import { ref, onMounted, onUnmounted, inject } from 'vue'
import { dashboardApi, type DashboardStats } from '../api/dashboard'

const stats = ref<DashboardStats | null>(null)
const signalr: any = inject('signalr')

const realtimeStats = ref({
  totalPnl: 0,
  dailyPnl: 0,
  winRate: 0
})

const riskAlert = ref('')

onMounted(async () => {
  try {
    const { data } = await dashboardApi.getStats()
    stats.value = data
  } catch {
    // ignore
  }

  if (signalr?.connected?.value) {
    signalr.on('DashboardSummary', (data: any) => {
      realtimeStats.value = {
        totalPnl: data.totalPnl,
        dailyPnl: data.dailyPnl,
        winRate: data.winRate
      }
      if (stats.value) {
        stats.value.openPositionCount = data.totalPositions
        stats.value.activeStrategyCount = data.activeStrategies
      }
    })

    signalr.on('RiskAlert', (data: any) => {
      riskAlert.value = `[${data.level}] ${data.message}`
      setTimeout(() => { riskAlert.value = '' }, 5000)
    })
  }
})

onUnmounted(() => {
  if (signalr) {
    signalr.off('DashboardSummary')
    signalr.off('RiskAlert')
  }
})
</script>

<template>
  <div class="dashboard">
    <div v-if="riskAlert" class="risk-alert">{{ riskAlert }}</div>

    <h2>仪表盘</h2>
    <p class="subtitle">欢迎使用 TradeX 多交易所现货交易系统</p>

    <div v-if="signalr?.connected?.value && realtimeStats.totalPnl !== 0" class="realtime-banner">
      实时: 总盈亏 <strong :class="realtimeStats.totalPnl >= 0 ? 'positive' : 'negative'">
        {{ realtimeStats.totalPnl >= 0 ? '+' : '' }}{{ realtimeStats.totalPnl.toFixed(2) }}
      </strong>
      | 胜率 {{ realtimeStats.winRate.toFixed(1) }}%
    </div>

    <div v-if="stats" class="stats-grid">
      <div class="stat-card">
        <span class="stat-value">{{ stats.traderCount }}</span>
        <span class="stat-label">交易员</span>
      </div>
      <div class="stat-card">
        <span class="stat-value">{{ stats.strategyCount }}</span>
        <span class="stat-label">策略总数</span>
      </div>
      <div class="stat-card">
        <span class="stat-value stat-active">{{ stats.activeStrategyCount }}</span>
        <span class="stat-label">活跃策略</span>
      </div>
      <div class="stat-card">
        <span class="stat-value stat-open">{{ stats.openPositionCount }}</span>
        <span class="stat-label">持仓中</span>
      </div>
      <div class="stat-card">
        <span class="stat-value stat-today">{{ stats.todayOrderCount }}</span>
        <span class="stat-label">今日订单</span>
      </div>
    </div>

    <div class="quick-links">
      <router-link to="/traders" class="card">
        <h3>交易员管理</h3>
        <p>创建和管理交易员账户</p>
      </router-link>
      <router-link to="/traders" class="card">
        <h3>交易所账户</h3>
        <p>连接和管理交易所 API</p>
      </router-link>
      <router-link to="/traders" class="card">
        <h3>交易策略</h3>
        <p>创建和监控策略运行</p>
      </router-link>
      <router-link to="/mfa/setup" class="card card-accent">
        <h3>安全设置</h3>
        <p>设置双重认证 (MFA)</p>
      </router-link>
    </div>
  </div>
</template>

<style scoped>
.dashboard { padding: 2rem; position: relative; }
h2 { margin: 0 0 0.25rem; color: #e2e8f0; }
.subtitle { color: #94a3b8; margin: 0 0 2rem; font-size: 0.9rem; }

.risk-alert {
  position: fixed;
  top: 1rem;
  right: 1rem;
  background: #7f1d1d;
  color: #fca5a5;
  padding: 0.75rem 1.25rem;
  border-radius: 6px;
  border: 1px solid #ef4444;
  font-size: 0.85rem;
  z-index: 200;
  animation: slideIn 0.3s ease;
}
@keyframes slideIn {
  from { transform: translateX(100%); opacity: 0; }
  to { transform: translateX(0); opacity: 1; }
}

.realtime-banner {
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 6px;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
  font-size: 0.85rem;
  color: #94a3b8;
}
.realtime-banner strong { font-size: 1rem; }
.positive { color: #22c55e; }
.negative { color: #ef4444; }

.stats-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(180px, 1fr)); gap: 1rem; margin-bottom: 2rem; }
.stat-card {
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 8px;
  padding: 1.5rem;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.5rem;
}
.stat-value { font-size: 2rem; font-weight: 700; color: #e2e8f0; }
.stat-label { font-size: 0.85rem; color: #64748b; text-transform: uppercase; letter-spacing: 0.05em; }
.stat-active { color: #38bdf8; }
.stat-open { color: #22c55e; }
.stat-today { color: #f59e0b; }

.quick-links { display: grid; grid-template-columns: repeat(auto-fill, minmax(220px, 1fr)); gap: 1rem; }
.card {
  display: block;
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 8px;
  padding: 1.5rem;
  text-decoration: none;
  transition: border-color 0.2s;
}
.card:hover { border-color: #38bdf8; }
.card h3 { color: #e2e8f0; margin: 0 0 0.5rem; font-size: 1rem; }
.card p { color: #64748b; margin: 0; font-size: 0.85rem; }
.card-accent { border-color: #38bdf8; }
.card-accent h3 { color: #38bdf8; }
</style>
