<script setup lang="ts">
import { computed, inject, onMounted, onUnmounted, ref } from 'vue'
import { Message } from '@arco-design/web-vue'
import { dashboardApi, type DashboardStats, type DashboardTrade } from '../api/dashboard'
import { exchangesApi } from '../api/exchanges'
import { getExchangeInfo } from '../api/exchangeInfo'
import { formatSmallNumber } from '../utils/format'

const stats = ref<DashboardStats | null>(null)
const loading = ref(true)
const loadError = ref('')
const exchangeAssets = ref<Record<string, { currency: string; balance: number }[]>>({})
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

const riskMeta = computed(() => {
  const status = stats.value?.riskStatus ?? 'Normal'
  if (status === 'Normal') return { label: '风险正常', type: 'success' as const }
  if (status === 'Warning') return { label: '需要关注', type: 'warning' as const }
  return { label: '高风险', type: 'danger' as const }
})

const exchangeEntries = computed(() => Object.entries(stats.value?.exchangeStatus ?? {}))

const primaryMetrics = computed(() => [
  {
    title: '总资产估值',
    value: formatCurrency(stats.value?.totalBalance ?? 0),
    suffix: '按当前持仓价格估算',
    tone: 'blue'
  },
  {
    title: '总盈亏',
    value: formatSignedCurrency(stats.value?.totalPnl ?? realtimeStats.value.totalPnl),
    suffix: `${formatPercent(stats.value?.totalPnlPercent ?? 0)} 总收益率`,
    tone: pnlTone.value
  },
  {
    title: '活跃策略',
    value: String(stats.value?.activeStrategyCount ?? realtimeStats.value.activeStrategies),
    suffix: `${stats.value?.strategyCount ?? 0} 个策略部署`,
    tone: 'green'
  },
  {
    title: '开放持仓',
    value: String(stats.value?.openPositionCount ?? realtimeStats.value.totalPositions),
    suffix: `${stats.value?.todayOrderCount ?? 0} 笔今日订单`,
    tone: 'amber'
  }
])

const pnlTone = computed(() => {
  const pnl = stats.value?.totalPnl ?? realtimeStats.value.totalPnl
  if (pnl > 0) return 'green'
  if (pnl < 0) return 'red'
  return 'neutral'
})

const systemCards = computed(() => [
  { label: '交易员', value: stats.value?.traderCount ?? 0, path: '/traders' },
  { label: '策略部署', value: stats.value?.strategyCount ?? 0, path: '/strategies' },
  { label: '交易所', value: exchangeEntries.value.length, path: '/exchanges' }
])

const exchangeListData = computed(() =>
  exchangeEntries.value.map(([exchange, status]) => {
    const info = getExchangeInfo(exchange)
    return {
      exchange: exchange.toUpperCase(),
      connected: status === 'Connected',
      statusLabel: status === 'Connected' ? '已连接' : '未连接',
      coins: exchangeAssets.value[exchange],
      icon: info.icon,
      color: info.color
    }
  })
)

async function loadStats() {
  loading.value = true
  loadError.value = ''
  try {
    const { data } = await dashboardApi.getStats()
    stats.value = data
  } catch (e: any) {
    const msg = e?.response?.data?.message || e?.message || '仪表盘数据加载失败'
    loadError.value = msg
    console.error('[Dashboard] loadStats error:', msg, e)
    Message.error({ content: msg, duration: 4000 })
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
    const level = (data.level ?? '').toLowerCase()
    const content = `[${data.level}] ${data.message}`
    if (level === 'high' || level === 'critical') {
      Message.error({ content, duration: 5000 })
    } else if (level === 'warning' || level === 'medium') {
      Message.warning({ content, duration: 4000 })
    } else {
      Message.info({ content, duration: 3000 })
    }
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
  <div>
    <!-- Hero -->
    <a-row :gutter="20" :style="{ marginBottom: '16px' }">
        <a-col :flex="1">
          <a-card :bordered="true" :style="{ height: '100%' }">
            <a-typography-text type="primary" :style="{ fontSize: '12px', fontWeight: 600, letterSpacing: '0.16em' }">
              TradeX COMMAND CENTER
            </a-typography-text>
            <a-typography-title :heading="2" :style="{ marginTop: '8px', marginBottom: '12px', lineHeight: 0.95 }">
              交易运行总览
            </a-typography-title>
            <a-typography-text type="secondary">
              集中观察交易员、交易所连接、策略部署、持仓风险与最近成交。
            </a-typography-text>
          </a-card>
        </a-col>
        <a-col :flex="'280px'">
          <a-card :bordered="true" :style="{ height: '100%' }">
            <a-typography-text type="secondary" :style="{ fontSize: '12px' }">系统风险</a-typography-text>
            <a-typography-text
              :type="riskMeta.type"
              :style="{ display: 'block', fontSize: '26px', fontWeight: 700, marginTop: '8px' }"
            >
              {{ riskMeta.label }}
            </a-typography-text>
            <a-typography-text type="secondary" :style="{ fontSize: '12px' }">
              实时推送 {{ signalr?.connected?.value ? '已连接' : '未连接' }}
            </a-typography-text>
          </a-card>
        </a-col>
      </a-row>

      <!-- Loading -->
      <a-row :gutter="16" v-if="loading">
        <a-col :span="6" v-for="i in 4" :key="i">
          <a-card :bordered="true">
            <a-skeleton :animation="true">
              <a-skeleton-line :rows="3" />
            </a-skeleton>
          </a-card>
        </a-col>
      </a-row>

      <!-- Content -->
      <template v-else>
        <!-- Metrics -->
        <a-row :gutter="16" :style="{ marginBottom: '16px' }">
          <a-col :xs="24" :sm="12" :md="6" v-for="m in primaryMetrics" :key="m.title">
            <a-card :bordered="true">
              <a-typography-text type="secondary" :style="{ fontSize: '13px' }">{{ m.title }}</a-typography-text>
              <div :style="{ fontSize: '28px', fontWeight: 700, marginTop: '4px' }">{{ m.value }}</div>
              <a-typography-text type="secondary" :style="{ fontSize: '12px', marginTop: '4px', display: 'block' }">
              {{ m.suffix }}
            </a-typography-text>
          </a-card>
        </a-col>
      </a-row>

      <!-- Realtime -->
      <a-card v-if="hasRealtime" :bordered="true" :style="{ marginBottom: '16px' }">
        <a-row :gutter="48">
          <a-col :span="6">
            <a-statistic
              title="实时盈亏"
              :value="realtimeStats.totalPnl"
              :precision="2"
              :prefix="realtimeStats.totalPnl >= 0 ? '+' : ''"
              :value-style="{ color: realtimeStats.totalPnl >= 0 ? 'rgb(var(--success-6))' : 'rgb(var(--danger-6))' }"
            />
          </a-col>
          <a-col :span="6">
            <a-statistic
              title="今日盈亏"
              :value="realtimeStats.dailyPnl"
              :precision="2"
              :prefix="realtimeStats.dailyPnl >= 0 ? '+' : ''"
              :value-style="{ color: realtimeStats.dailyPnl >= 0 ? 'rgb(var(--success-6))' : 'rgb(var(--danger-6))' }"
            />
          </a-col>
          <a-col :span="6">
            <a-statistic
              title="胜率"
              :value="realtimeStats.winRate"
              :precision="1"
              suffix="%"
            />
          </a-col>
          <a-col :span="6" :style="{ display: 'flex', alignItems: 'flex-end', justifyContent: 'flex-end' }">
            <a-typography-text type="secondary" :style="{ fontSize: '12px' }">
              更新于 {{ formatTime(realtimeStats.lastUpdateAtUtc) }}
            </a-typography-text>
          </a-col>
        </a-row>
      </a-card>

      <!-- Main content grid -->
      <a-row :gutter="16" :style="{ marginBottom: '16px' }">
        <!-- Operations -->
        <a-col :xs="24" :md="12" :style="{ display: 'flex' }">
          <a-card title="运行资源" :bordered="true" :style="{ flex: 1 }">
            <template #extra>
              <a-typography-text type="primary" :style="{ fontSize: '11px', fontWeight: 600, letterSpacing: '0.16em' }">
                OPERATIONS
              </a-typography-text>
            </template>

            <a-row :gutter="12" :style="{ marginBottom: '16px' }">
              <a-col :span="8" v-for="card in systemCards" :key="card.label">
                <a-card
                  hoverable
                  size="small"
                  :bordered="true"
                  @click="$router.push(card.path)"
                >
                  <a-typography-text type="secondary">{{ card.label }}</a-typography-text>
                  <a-typography-title :heading="3" :style="{ margin: '4px 0 0' }">
                    {{ card.value }}
                  </a-typography-title>
                </a-card>
              </a-col>
            </a-row>

            <a-divider :style="{ margin: '0 0 12px' }" />

            <a-typography-text bold :style="{ display: 'block', marginBottom: '12px' }">交易所连接</a-typography-text>

            <a-row :gutter="12" v-if="exchangeListData.length">
              <a-col :span="8" v-for="item in exchangeListData" :key="item.exchange">
                <a-card
                  size="small"
                  :bordered="true"
                  :style="{ borderLeft: item.connected ? '3px solid rgb(var(--success-6))' : '3px solid rgb(var(--danger-6))' }"
                >
                  <a-space direction="vertical" :size="4" fill>
                    <a-space :size="8">
                      <img
                         :src="item.icon"
                         :alt="item.exchange"
                         :style="{ width: '20px', height: '20px', flexShrink: 0 }"
                       />
                      <a-typography-text bold>{{ item.exchange }}</a-typography-text>
                      <a-tag :color="item.connected ? 'green' : 'gray'" size="small">{{ item.statusLabel }}</a-tag>
                    </a-space>
                    <template v-if="item.coins">
                      <a-popover
                        position="bottom"
                        trigger="hover"
                        :style="{ padding: 0 }"
                      >
                        <template #title>
                          <a-typography-text bold>资产明细</a-typography-text>
                        </template>
                        <template #content>
                          <div :style="{ maxHeight: '240px', overflowY: 'auto', minWidth: '200px' }">
                            <div
                              v-for="coin in item.coins"
                              :key="coin.currency"
                              :style="{
                                display: 'flex',
                                justifyContent: 'space-between',
                                alignItems: 'center',
                                padding: '6px 0',
                                borderBottom: '1px solid var(--color-border-2)'
                              }"
                            >
                              <a-space :size="8">
                                <div
                                  :style="{
                                    width: '20px',
                                    height: '20px',
                                    borderRadius: '50%',
                                    background: 'var(--color-fill-3)',
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'center',
                                    fontSize: '10px',
                                    fontWeight: 600,
                                    color: 'var(--color-text-2)',
                                    flexShrink: 0
                                  }"
                                >
                                  {{ coin.currency.charAt(0) }}
                                </div>
                                <a-typography-text>{{ coin.currency }}</a-typography-text>
                              </a-space>
                              <a-typography-text type="secondary" :style="{ fontVariantNumeric: 'tabular-nums' }">
                                {{ coin.balance.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 8 }) }}
                              </a-typography-text>
                            </div>
                          </div>
                        </template>
                        <a-link :style="{ fontSize: '12px' }">{{ item.coins.length }} 个币种</a-link>
                      </a-popover>
                    </template>
                  </a-space>
                </a-card>
              </a-col>
            </a-row>
            <a-empty v-else description="暂无已启用交易所" />

          </a-card>
        </a-col>

        <!-- Trades -->
        <a-col :xs="24" :md="12" :style="{ display: 'flex' }">
          <a-card title="最近成交" :bordered="true" :style="{ flex: 1 }">
            <template #extra>
              <a-button type="text" size="small" @click="$router.push('/traders')">查看交易员</a-button>
            </template>

            <a-table
              v-if="stats?.recentTrades?.length"
              :data="stats.recentTrades"
              :pagination="false"
              :bordered="false"
              size="small"
              :row-style="{ cursor: 'pointer' }"
            >
              <template #columns>
                <a-table-column title="交易对" data-index="pair">
                  <template #cell="{ record }">
                    <a-typography-text bold>{{ record.pair }}</a-typography-text>
                  </template>
                </a-table-column>
                <a-table-column title="方向" data-index="side" :width="80">
                  <template #cell="{ record }">
                    <a-tag :color="record.side === 'Buy' ? 'green' : 'red'" size="small">
                      {{ formatTradeSide(record.side) }}
                    </a-tag>
                  </template>
                </a-table-column>
                <a-table-column title="价格" :width="120">
                  <template #cell="{ record }">
                    <a-typography-text>{{ formatTradePrice(record) }}</a-typography-text>
                    <a-typography-text type="secondary" :style="{ display: 'block', fontSize: '12px' }">
                      {{ formatSmallNumber(record.quantity) }}
                    </a-typography-text>
                  </template>
                </a-table-column>
                <a-table-column title="时间" data-index="placedAtUtc" :width="160">
                  <template #cell="{ record }">
                    <a-typography-text type="secondary" :style="{ fontSize: '12px' }">
                      {{ formatTime(record.placedAtUtc) }}
                    </a-typography-text>
                  </template>
                </a-table-column>
              </template>
            </a-table>
            <a-empty v-else description="暂无成交记录">
              <a-typography-text type="secondary">策略开始运行并产生成交后，会在这里显示最新订单。</a-typography-text>
            </a-empty>
          </a-card>
        </a-col>
      </a-row>

      <!-- Quick Actions -->
      <a-row :gutter="16">
        <a-col :xs="24" :sm="12" :md="6" v-for="action in [
          { icon: 'user', label: '管理交易员', desc: '创建交易员并进入策略、持仓、订单视图', path: '/traders' },
          { icon: 'exchange', label: '连接交易所', desc: '配置 API Key、测试连接并拉取交易对规则', path: '/exchanges' },
          { icon: 'common', label: '编辑策略模板', desc: '维护条件树、执行规则与风险参数', path: '/strategies' },
          { icon: 'file', label: '查看审计日志', desc: '追踪关键操作、请求路径与状态码', path: '/audit-logs' }
        ]" :key="action.label">
          <a-card hoverable :bordered="true" @click="$router.push(action.path)">
            <a-space direction="vertical" :size="8">
              <a-typography-text type="primary" bold>
                <icon-common /> {{ action.label.slice(0, 2) }}
              </a-typography-text>
              <a-typography-text bold>{{ action.label }}</a-typography-text>
              <a-typography-text type="secondary" :style="{ fontSize: '13px' }">
                {{ action.desc }}
              </a-typography-text>
            </a-space>
          </a-card>
        </a-col>
      </a-row>
      </template>
    </div>
</template>
