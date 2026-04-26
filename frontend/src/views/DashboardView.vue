<script setup lang="ts">
import { computed, inject, onMounted, onUnmounted, ref } from 'vue'
import { dashboardApi, type DashboardStats, type DashboardTrade } from '../api/dashboard'
import { exchangesApi } from '../api/exchanges'
import { formatSmallNumber } from '../utils/format'

const stats = ref<DashboardStats | null>(null)
const loading = ref(true)
const loadError = ref('')
const riskAlert = ref('')
const exchangeAssets = ref<Record<string, { currency: string; balance: number }[]>>({})
const expandedExchange = ref<Record<string, boolean>>({})
const signalr: any = inject('signalr')

const realtimeStats = ref({
  totalPnl: 0,
  dailyPnl: 0,
  winRate: 0,
  totalPositions: 0,
  activeStrategies: 0,
  lastUpdateAtUtc: ''
})

const hasRealtime = computed(() => Boolean(realtimeStats.value.lastUpdateAtUtc))

const pnlClass = computed(() => {
  const pnl = stats.value?.totalPnl ?? realtimeStats.value.totalPnl
  if (pnl > 0) return 'positive'
  if (pnl < 0) return 'negative'
  return 'neutral'
})

const riskMeta = computed(() => {
  const status = stats.value?.riskStatus ?? 'Normal'
  if (status === 'Normal') return { label: '风险正常', className: 'risk-normal' }
  if (status === 'Warning') return { label: '需要关注', className: 'risk-warning' }
  return { label: '高风险', className: 'risk-danger' }
})

const exchangeEntries = computed(() => Object.entries(stats.value?.exchangeStatus ?? {}))

const primaryMetrics = computed(() => [
  {
    label: '总资产估值',
    value: formatCurrency(stats.value?.totalBalance ?? 0),
    hint: '按当前持仓价格估算',
    tone: 'blue'
  },
  {
    label: '总盈亏',
    value: formatSignedCurrency(stats.value?.totalPnl ?? realtimeStats.value.totalPnl),
    hint: `${formatPercent(stats.value?.totalPnlPercent ?? 0)} 总收益率`,
    tone: pnlClass.value
  },
  {
    label: '活跃策略',
    value: String(stats.value?.activeStrategyCount ?? realtimeStats.value.activeStrategies),
    hint: `${stats.value?.strategyCount ?? 0} 个策略部署`,
    tone: 'green'
  },
  {
    label: '开放持仓',
    value: String(stats.value?.openPositionCount ?? realtimeStats.value.totalPositions),
    hint: `${stats.value?.todayOrderCount ?? 0} 笔今日订单`,
    tone: 'amber'
  }
])

const systemCards = computed(() => [
  { label: '交易员', value: stats.value?.traderCount ?? 0, path: '/traders' },
  { label: '策略部署', value: stats.value?.strategyCount ?? 0, path: '/strategies' },
  { label: '交易所', value: exchangeEntries.value.length, path: '/exchanges' }
])

async function loadStats() {
  loading.value = true
  loadError.value = ''
  try {
    const { data } = await dashboardApi.getStats()
    stats.value = data
  } catch {
    loadError.value = '仪表盘数据加载失败，请稍后重试'
  } finally {
    loading.value = false
  }
}

async function fetchExchangeBalances() {
  try {
    const { data } = await exchangesApi.getAll()
    const enabled = (data.data ?? []).filter((a: any) => a.isEnabled)
    const results = await Promise.allSettled(
      enabled.map((a: any) =>
        exchangesApi.getAssets(a.id).then(r => ({ id: a.exchangeType.toLowerCase(), items: r.data.data }))
      )
    )
    for (const r of results) {
      if (r.status === 'fulfilled') {
        exchangeAssets.value[r.value.id] = r.value.items
      }
    }
  } catch { /* ignore */ }
}

function bindRealtime() {
  if (!signalr?.connected?.value) return

  signalr.on('DashboardSummary', (data: any) => {
    realtimeStats.value = {
      totalPnl: Number(data.totalPnl ?? 0),
      dailyPnl: Number(data.dailyPnl ?? 0),
      winRate: Number(data.winRate ?? 0),
      totalPositions: Number(data.totalPositions ?? 0),
      activeStrategies: Number(data.activeStrategies ?? 0),
      lastUpdateAtUtc: data.lastUpdateAtUtc ?? new Date().toISOString()
    }

    if (stats.value) {
      stats.value.totalPnl = realtimeStats.value.totalPnl
      stats.value.openPositionCount = realtimeStats.value.totalPositions
      stats.value.activeStrategyCount = realtimeStats.value.activeStrategies
    }
  })

  signalr.on('RiskAlert', (data: any) => {
    riskAlert.value = `[${data.level}] ${data.message}`
    setTimeout(() => { riskAlert.value = '' }, 5000)
  })
}

function formatCurrency(value: number): string {
  return `$${formatSmallNumber(value)}`
}

function formatSignedCurrency(value: number): string {
  const sign = value > 0 ? '+' : ''
  return `${sign}${formatCurrency(value)}`
}

function formatPercent(value: number): string {
  const sign = value > 0 ? '+' : ''
  return `${sign}${value.toFixed(2)}%`
}

function formatTradePrice(trade: DashboardTrade): string {
  if (!trade.price) return '市价'
  return formatCurrency(trade.price)
}

function formatTradeSide(side: string): string {
  return side === 'Buy' ? '买入' : side === 'Sell' ? '卖出' : side
}

function formatTime(value: string): string {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return '-'
  return date.toLocaleString('zh-CN', { hour12: false })
}

onMounted(async () => {
  await loadStats()
  await fetchExchangeBalances()
  bindRealtime()
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

    <section class="hero-panel">
      <div class="hero-copy">
        <span class="eyebrow">TradeX Command Center</span>
        <h1>交易运行总览</h1>
        <p>集中观察交易员、交易所连接、策略部署、持仓风险与最近成交。</p>
      </div>
      <div class="hero-status" :class="riskMeta.className">
        <span class="status-label">系统风险</span>
        <strong>{{ riskMeta.label }}</strong>
        <small>实时推送 {{ signalr?.connected?.value ? '已连接' : '未连接' }}</small>
      </div>
    </section>

    <div v-if="loadError" class="error-banner">
      <span>{{ loadError }}</span>
      <AppButton variant="danger" size="sm" icon="refresh" @click="loadStats">重试</AppButton>
    </div>

    <div v-if="loading" class="skeleton-grid">
      <div v-for="i in 4" :key="i" class="skeleton-card" />
    </div>

    <template v-else>
      <section class="metrics-grid">
        <AppCard v-for="metric in primaryMetrics" :key="metric.label" class="metric-card" :class="`tone-${metric.tone}`">
          <span class="metric-label">{{ metric.label }}</span>
          <strong>{{ metric.value }}</strong>
          <small>{{ metric.hint }}</small>
        </AppCard>
      </section>

      <section v-if="hasRealtime" class="realtime-strip">
        <div>
          <span>实时盈亏</span>
          <strong :class="realtimeStats.totalPnl >= 0 ? 'positive' : 'negative'">
            {{ formatSignedCurrency(realtimeStats.totalPnl) }}
          </strong>
        </div>
        <div>
          <span>今日盈亏</span>
          <strong :class="realtimeStats.dailyPnl >= 0 ? 'positive' : 'negative'">
            {{ formatSignedCurrency(realtimeStats.dailyPnl) }}
          </strong>
        </div>
        <div>
          <span>胜率</span>
          <strong>{{ realtimeStats.winRate.toFixed(1) }}%</strong>
        </div>
        <small>更新于 {{ formatTime(realtimeStats.lastUpdateAtUtc) }}</small>
      </section>

      <section class="content-grid">
        <AppCard class="panel operations-panel">
          <header class="panel-header">
            <div>
              <span class="eyebrow">Operations</span>
              <h2>运行资源</h2>
            </div>
          </header>

          <div class="system-card-list">
            <AppCard v-for="card in systemCards" :key="card.label" :to="card.path" class="system-card" variant="subtle" padding="sm">
              <span>{{ card.label }}</span>
              <strong>{{ card.value }}</strong>
            </AppCard>
          </div>

          <div class="exchange-list">
            <h3>交易所连接</h3>
            <div v-if="exchangeEntries.length" class="exchange-items">
              <template v-for="[exchange, status] in exchangeEntries" :key="exchange">
                <div class="exchange-item">
                  <span class="exchange-dot" :class="status === 'Connected' ? 'connected' : 'disconnected'" />
                  <span class="exchange-name">{{ exchange.toUpperCase() }}</span>
                  <span v-if="exchangeAssets[exchange]" class="exchange-coins" @click="expandedExchange[exchange] = !expandedExchange[exchange]">
                    {{ exchangeAssets[exchange].length }} 个币种
                    <span class="expand-arrow" :class="{ expanded: expandedExchange[exchange] }">▶</span>
                  </span>
                  <strong :class="status === 'Connected' ? 'status-online' : 'status-offline'">{{ status === 'Connected' ? '已连接' : '未连接' }}</strong>
                </div>
                <div v-if="expandedExchange[exchange] && exchangeAssets[exchange]" class="asset-list">
                  <div v-for="item in exchangeAssets[exchange]" :key="item.currency" class="asset-row">
                    <span class="asset-currency">{{ item.currency }}</span>
                    <span class="asset-amount">{{ item.balance.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 8 }) }}</span>
                  </div>
                </div>
              </template>
            </div>
            <div v-else class="empty-state">暂无已启用交易所</div>
          </div>
        </AppCard>

        <AppCard class="panel trades-panel">
          <header class="panel-header">
            <div>
              <span class="eyebrow">Recent Fills</span>
              <h2>最近成交</h2>
            </div>
            <AppButton variant="ghost" size="sm" icon="user" @click="$router.push('/traders')">查看交易员</AppButton>
          </header>

          <div v-if="stats?.recentTrades?.length" class="trade-list">
            <div v-for="trade in stats.recentTrades" :key="trade.orderId" class="trade-row">
              <div>
                <strong>{{ trade.symbolId }}</strong>
                <span>{{ formatTime(trade.placedAtUtc) }}</span>
              </div>
              <div class="trade-side" :class="trade.side === 'Buy' ? 'buy' : 'sell'">{{ formatTradeSide(trade.side) }}</div>
              <div class="trade-value">
                <strong>{{ formatTradePrice(trade) }}</strong>
                <span>{{ formatSmallNumber(trade.quantity) }}</span>
              </div>
            </div>
          </div>
          <div v-else class="empty-state large">
            <strong>暂无成交记录</strong>
            <span>策略开始运行并产生成交后，会在这里显示最新订单。</span>
          </div>
        </AppCard>
      </section>

      <section class="quick-actions">
        <AppCard to="/traders" class="action-card" interactive>
          <span><AppIcon name="user" />01</span>
          <strong>管理交易员</strong>
          <small>创建交易员并进入策略、持仓、订单视图</small>
        </AppCard>
        <AppCard to="/exchanges" class="action-card" interactive>
          <span><AppIcon name="exchange" />02</span>
          <strong>连接交易所</strong>
          <small>配置 API Key、测试连接并拉取交易对规则</small>
        </AppCard>
        <AppCard to="/strategies" class="action-card" interactive>
          <span><AppIcon name="strategy" />03</span>
          <strong>编辑策略模板</strong>
          <small>维护条件树、执行规则与风险参数</small>
        </AppCard>
        <AppCard to="/audit-logs" class="action-card" interactive>
          <span><AppIcon name="table" />04</span>
          <strong>查看审计日志</strong>
          <small>追踪关键操作、请求路径与状态码</small>
        </AppCard>
      </section>
    </template>
  </div>
</template>

<style scoped>
.dashboard {
  min-height: 100%;
  padding: 2rem;
  position: relative;
  color: var(--text-primary);
}

.hero-panel {
  display: grid;
  grid-template-columns: minmax(0, 1fr) 280px;
  gap: 1.25rem;
  padding: 1.5rem;
  border: 1px solid rgba(0, 0, 0, 0.06);
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.80);
  box-shadow: 0 8px 30px rgba(139, 119, 88, 0.06);
  margin-bottom: 1rem;
  position: relative;
}

.hero-panel::before { display: none; }
.hero-panel::after { display: none; }

.hero-copy h1 {
  margin: 0.2rem 0 0.5rem;
  font-size: clamp(2rem, 5vw, 4.5rem);
  line-height: 0.95;
  letter-spacing: -0.06em;
}

.hero-copy p {
  margin: 0;
  color: var(--text-muted);
  max-width: 620px;
}

.eyebrow {
  color: var(--accent-blue);
  font-size: 0.72rem;
  letter-spacing: 0.16em;
  text-transform: uppercase;
  font-weight: 700;
}

.hero-status {
  display: flex;
  flex-direction: column;
  justify-content: flex-end;
  gap: 0.45rem;
  min-height: 180px;
  padding: 1rem;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.50);
  border: 1px solid rgba(0, 0, 0, 0.04);
}

.hero-status strong { font-size: 1.6rem; }
.hero-status small, .status-label { color: var(--text-muted); }
.risk-normal strong { color: var(--accent-green); }
.risk-warning strong { color: var(--accent-amber); }
.risk-danger strong { color: var(--accent-red); }

.risk-alert {
  position: fixed;
  top: 1rem;
  right: 1rem;
  background: rgba(191, 72, 72, 0.12);
  color: #7a3a3a;
  padding: 0.75rem 1.25rem;
  border-radius: 6px;
  border: 1px solid rgba(191, 72, 72, 0.28);
  font-size: 0.85rem;
  z-index: 200;
  box-shadow: 0 18px 45px rgba(139, 119, 88, 0.14);
}

.error-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  padding: 0.85rem 1rem;
  margin-bottom: 1rem;
  border-radius: 6px;
  background: rgba(191, 72, 72, 0.06);
  border: 1px solid rgba(191, 72, 72, 0.18);
  color: #7a3a3a;
}

.error-banner button {
  border: 1px solid rgba(79, 126, 201, 0.34);
  border-radius: 999px;
  background: rgba(79, 126, 201, 0.06);
  color: var(--accent-blue);
  padding: 0.35rem 0.75rem;
  text-decoration: none;
  cursor: pointer;
}

.error-banner button {
  border: 1px solid rgba(79, 126, 201, 0.45);
  border-radius: 999px;
  background: rgba(79, 126, 201, 0.10);
  color: var(--accent-blue);
  padding: 0.35rem 0.75rem;
  text-decoration: none;
  cursor: pointer;
}

.metrics-grid,
.skeleton-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 1rem;
  margin-bottom: 1rem;
}

.skeleton-card {
  min-height: 126px;
  padding: 1.1rem;
  border-radius: 6px;
  background:
    linear-gradient(145deg, rgba(255, 255, 255, 0.13), rgba(255, 255, 255, 0.035) 42%, rgba(255, 255, 255, 0.045)),
    rgba(255, 255, 255, 0.035);
  border: 1px solid var(--glass-border);
  box-shadow: inset 0 1px 0 var(--glass-highlight), 0 14px 40px rgba(2, 6, 23, 0.14);
  backdrop-filter: blur(34px) saturate(180%) brightness(1.06);
  -webkit-backdrop-filter: blur(34px) saturate(180%) brightness(1.06);
  position: relative;
  overflow: hidden;
}

.realtime-strip > * { position: relative; }

.metric-card {
  min-height: 126px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
}

.metric-card strong {
  font-size: clamp(1.55rem, 3vw, 2.25rem);
  letter-spacing: -0.04em;
}

.metric-card small,
.metric-label { color: var(--text-muted); }
.tone-blue strong { color: var(--accent-blue); }
.tone-green strong, .positive { color: var(--accent-green); }
.tone-amber strong { color: var(--accent-amber); }
.tone-negative strong, .negative { color: var(--accent-red); }
.tone-neutral strong, .neutral { color: var(--text-primary); }

.skeleton-card {
  background: linear-gradient(90deg, rgba(15, 23, 42, 0.44) 0%, rgba(255, 255, 255, 0.10) 50%, rgba(15, 23, 42, 0.44) 100%);
  background-size: 200% 100%;
  animation: pulse 1.1s ease-in-out infinite;
}

@keyframes pulse {
  from { background-position: 0 0; }
  to { background-position: -200% 0; }
}

.realtime-strip {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr)) auto;
  align-items: center;
  gap: 1rem;
  padding: 0.85rem 1rem;
  margin-bottom: 1rem;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.60);
  border: 1px solid rgba(0, 0, 0, 0.04);
}

.realtime-strip div {
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.realtime-strip span,
.realtime-strip small { color: var(--text-muted); font-size: 0.78rem; }

.content-grid {
  display: grid;
  grid-template-columns: minmax(320px, 0.9fr) minmax(0, 1.1fr);
  gap: 1rem;
  margin-bottom: 1rem;
}

.panel {
  min-width: 0;
}

.panel-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 1rem;
  margin-bottom: 1rem;
}

.panel-header h2,
.exchange-list h3 {
  margin: 0.15rem 0 0;
  font-size: 1.1rem;
}

.system-card-list {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.system-card {
  border-radius: 6px;
  color: var(--text-muted);
}

.system-card strong {
  display: block;
  margin-top: 0.35rem;
  color: var(--text-primary);
  font-size: 1.55rem;
}

.exchange-items,
.trade-list {
  display: flex;
  flex-direction: column;
  gap: 0.65rem;
}

.exchange-item,
.trade-row {
  display: grid;
  align-items: center;
  gap: 0.75rem;
  padding: 0.75rem;
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.046);
  border: 1px solid var(--glass-border);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.045);
}

.exchange-item { grid-template-columns: auto 1fr auto auto; gap: 0.75rem; }
.trade-row { grid-template-columns: minmax(0, 1fr) auto auto; }

.exchange-dot {
  width: 0.55rem;
  height: 0.55rem;
  border-radius: 50%;
}

.exchange-dot.connected { background: var(--accent-green); }
.exchange-dot.disconnected { background: var(--accent-red); }
.exchange-name { font-weight: 500; }
.exchange-balance { color: var(--text-secondary); font-size: 0.8rem; white-space: nowrap; font-weight: 500; }
.status-online { color: var(--accent-green); font-weight: 600; }
.status-offline { color: var(--text-muted); }

.exchange-coins {
  cursor: pointer;
  color: var(--accent-blue);
  font-size: 0.8rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 0.25rem;
  user-select: none;
}
.exchange-coins:hover { opacity: 0.8; }

.expand-arrow {
  font-size: 0.55rem;
  transition: transform 0.2s ease;
  display: inline-block;
}
.expand-arrow.expanded { transform: rotate(90deg); }

.asset-list {
  padding: 0.25rem 0.75rem 0.5rem 1.25rem;
  border-bottom: 1px solid var(--glass-border);
}
.asset-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.15rem 0;
  font-size: 0.8rem;
}
.asset-currency { color: var(--text-primary); font-weight: 500; }
.asset-amount { color: var(--text-muted); font-variant-numeric: tabular-nums; }

.trade-row strong,
.trade-row span { display: block; }
.trade-row span { color: var(--text-muted); font-size: 0.78rem; margin-top: 0.15rem; }

.trade-side {
  padding: 0.2rem 0.55rem;
  border-radius: 999px;
  color: var(--text-primary);
  font-weight: 700;
  font-size: 0.75rem;
}

.trade-side.buy { background: var(--accent-green); }
.trade-side.sell { background: var(--accent-red); color: #fee2e2; }
.trade-value { text-align: right; }

.empty-state {
  padding: 1rem;
  border: 1px dashed var(--glass-border-strong);
  border-radius: 6px;
  color: var(--text-muted);
  text-align: center;
}

.empty-state.large {
  min-height: 180px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 0.35rem;
}

.empty-state strong { color: var(--text-secondary); }

.quick-actions {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 1rem;
}

.action-card {
  border-radius: 6px;
  color: var(--text-primary);
  min-height: 140px;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
}

.action-card span { color: var(--accent-blue); font-weight: 800; }
.action-card small { color: var(--text-muted); line-height: 1.45; }

@media (max-width: 1100px) {
  .metrics-grid,
  .skeleton-grid,
  .quick-actions { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .content-grid { grid-template-columns: 1fr; }
}

@media (max-width: 760px) {
  .dashboard { padding: 1rem; }
  .hero-panel { grid-template-columns: 1fr; border-radius: 6px; }
  .hero-status { min-height: auto; }
  .metrics-grid,
  .skeleton-grid,
  .quick-actions,
  .system-card-list,
  .realtime-strip { grid-template-columns: 1fr; }
  .trade-row { grid-template-columns: 1fr; align-items: flex-start; }
  .trade-value { text-align: left; }
}
</style>
