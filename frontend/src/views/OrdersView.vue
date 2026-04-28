<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ordersApi, type Order } from '../api/orders'
import { exchangesApi, type Exchange } from '../api/exchanges'

const route = useRoute()
const router = useRouter()
const traderId = route.params.traderId as string

const orders = ref<Order[]>([])
const exchanges = ref<Exchange[]>([])
const loading = ref(true)
const showForm = ref(false)
const submitting = ref(false)

const formExchangeId = ref('')
const formSymbolId = ref('')
const formSide = ref('Buy')
const formType = ref('Market')
const formQuantity = ref(0)
const formPrice = ref<number | undefined>(undefined)

const statusLabels: Record<string, string> = {
  Pending: '待成交',
  PartiallyFilled: '部分成交',
  Filled: '已成交',
  Cancelled: '已撤销',
  Failed: '失败'
}

const statusColors: Record<string, string> = {
  Pending: '#f59e0b',
  PartiallyFilled: 'var(--accent-blue)',
  Filled: 'var(--accent-green)',
  Cancelled: '#64748b',
  Failed: '#ef4444'
}

async function load() {
  loading.value = true
  try {
    const [orderRes, accRes] = await Promise.all([
      ordersApi.getAll(traderId),
      exchangesApi.getAll()
    ])
    orders.value = orderRes.data ?? []
    exchanges.value = accRes.data.data ?? []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  formExchangeId.value = exchanges.value[0]?.id ?? ''
  formSymbolId.value = ''
  formSide.value = 'Buy'
  formType.value = 'Market'
  formQuantity.value = 0
  formPrice.value = undefined
  showForm.value = true
}

async function submitOrder() {
  submitting.value = true
  try {
    await ordersApi.createManual(traderId, {
      exchangeId: formExchangeId.value,
      symbolId: formSymbolId.value.toUpperCase(),
      side: formSide.value,
      type: formType.value,
      quantity: formQuantity.value,
      price: formType.value === 'Limit' ? formPrice.value : undefined
    })
    showForm.value = false
    await load()
  } finally {
    submitting.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="orders-page">
    <header class="page-header">
      <div class="header-left">
        <a-button type="text" size="small" @click="router.push(`/traders/${traderId}/positions`)">
          <template #icon><icon-left /></template>
          持仓
        </a-button>
        <h2>订单记录</h2>
      </div>
      <a-button type="primary" @click="openCreate">
        <template #icon><icon-list /></template>
        手动下单
      </a-button>
    </header>

    <a-modal v-model:visible="showForm" title="手动下单" width="sm" :mask-closable="false">
      <div class="form-body">
        <a-select
          :model-value="formExchangeId"
          style="width: 100%"
          @change="(v) => formExchangeId = String(v)"
        >
          <a-option v-for="a in exchanges" :key="a.id" :value="a.id" :label="`${a.label} (${a.exchangeType})`" />
        </a-select>
        <a-input v-model="formSymbolId" placeholder="交易对，如 BTCUSDT" />
        <div class="form-row">
          <a-select :model-value="formSide" @change="(v) => formSide = String(v) as 'Buy' | 'Sell'">
            <a-option value="Buy" label="买入" />
            <a-option value="Sell" label="卖出" />
          </a-select>
          <a-select :model-value="formType" @change="(v) => formType = String(v) as 'Market' | 'Limit'">
            <a-option value="Market" label="市价" />
            <a-option value="Limit" label="限价" />
          </a-select>
        </div>
        <a-input-number v-model="formQuantity" :step="0.0001" placeholder="数量" style="width: 100%" />
        <a-input-number v-if="formType === 'Limit'" v-model="formPrice" :step="0.01" placeholder="价格" style="width: 100%" />
      </div>
      <template #footer>
        <a-button type="primary" :loading="submitting" @click="submitOrder">
          <template #icon><icon-play-arrow /></template>
          {{ submitting ? '提交中...' : '提交订单' }}
        </a-button>
      </template>
    </a-modal>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th>交易对</th>
          <th>方向</th>
          <th>类型</th>
          <th>状态</th>
          <th>价格</th>
          <th>数量</th>
          <th>已成交</th>
          <th>时间</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="o in orders" :key="o.id">
          <td>{{ o.symbolId }}</td>
          <td><span :class="o.side === 'Buy' ? 'side-buy' : 'side-sell'">{{ o.side === 'Buy' ? '买入' : '卖出' }}</span></td>
          <td>{{ o.type === 'Market' ? '市价' : o.type === 'Limit' ? '限价' : '止损限价' }}</td>
          <td><a-tag :color="statusColors[o.status]">{{ statusLabels[o.status] ?? o.status }}</a-tag></td>
          <td>{{ o.price && o.price > 0 ? o.price.toLocaleString() : '-' }}</td>
          <td>{{ o.quantity.toLocaleString() }}</td>
          <td>{{ o.filledQuantity.toLocaleString() }}</td>
          <td>{{ new Date(o.updatedAt).toLocaleString('zh-CN', { hour12: false }) }}</td>
        </tr>
        <tr v-if="orders.length === 0">
          <td colspan="8" class="empty">暂无订单</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.orders-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.header-left { display: flex; align-items: center; gap: 0.75rem; }
.header-left h2 { margin: 0; color: var(--text-primary); }
.loading { text-align: center; color: var(--text-muted); padding: 2rem; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.625rem 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); font-size: 0.85rem; }
.table th { color: var(--text-muted); font-weight: 600; }
.side-buy { color: var(--accent-green); font-weight: 600; }
.side-sell { color: var(--accent-red); font-weight: 600; }
.form-body { display: flex; flex-direction: column; gap: 0.75rem; }
.form-row { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; }
</style>
