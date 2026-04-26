<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ordersApi, type Order } from '../api/orders'
import { exchangesApi, type Exchange } from '../api/exchanges'
import { formatSmallNumber } from '../utils/format'
import AppSelect from '../components/AppSelect.vue'

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
        <AppButton variant="ghost" size="sm" icon="back" @click="router.push(`/traders/${traderId}/positions`)">持仓</AppButton>
        <h2>订单记录</h2>
      </div>
      <AppButton variant="primary" icon="orders" @click="openCreate">手动下单</AppButton>
    </header>

    <AppModal v-model="showForm" title="手动下单" width="sm">
      <AppSelect
        :options="exchanges.map(a => ({ label: `${a.label} (${a.exchangeType})`, value: a.id }))"
        :model-value="formExchangeId"
        full
        form
        @update:model-value="(v: string | number) => formExchangeId = String(v)"
      />
      <input v-model="formSymbolId" placeholder="交易对，如 BTCUSDT" class="input" />
      <div class="form-row">
        <AppSelect
          :options="[{ label: '买入', value: 'Buy' }, { label: '卖出', value: 'Sell' }]"
          :model-value="formSide"
          full
          form
          @update:model-value="(v: string | number) => formSide = v as 'Buy' | 'Sell'"
        />
        <AppSelect
          :options="[{ label: '市价', value: 'Market' }, { label: '限价', value: 'Limit' }]"
          :model-value="formType"
          full
          form
          @update:model-value="(v: string | number) => formType = v as 'Market' | 'Limit'"
        />
      </div>
      <input v-model.number="formQuantity" type="number" step="0.0001" placeholder="数量" class="input" />
      <input v-if="formType === 'Limit'" v-model.number="formPrice" type="number" step="0.01" placeholder="价格" class="input" />
      <template #footer>
        <AppButton icon="close" @click="showForm = false">取消</AppButton>
        <AppButton variant="primary" icon="play" :disabled="submitting" @click="submitOrder">{{ submitting ? '提交中...' : '提交订单' }}</AppButton>
      </template>
    </AppModal>

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
          <th>手续费</th>
          <th>手动</th>
          <th>下单时间</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="o in orders" :key="o.id">
          <td>{{ o.symbolId }}</td>
          <td>
            <span :class="o.side === 'Buy' ? 'side-buy' : 'side-sell'">
              {{ o.side === 'Buy' ? '买入' : '卖出' }}
            </span>
          </td>
          <td>{{ o.type === 'Market' ? '市价' : '限价' }}</td>
          <td>
            <span class="status-badge" :style="{ background: statusColors[o.status] }">
              {{ statusLabels[o.status] }}
            </span>
          </td>
          <td>{{ o.price != null ? formatSmallNumber(o.price) : '-' }}</td>
          <td>{{ formatSmallNumber(o.quantity) }}</td>
          <td>{{ formatSmallNumber(o.filledQuantity) }}</td>
          <td>{{ formatSmallNumber(o.fee) }} {{ o.feeAsset ?? '' }}</td>
          <td>{{ o.isManual ? '是' : '否' }}</td>
          <td>{{ new Date(o.placedAtUtc).toLocaleString() }}</td>
        </tr>
        <tr v-if="orders.length === 0">
          <td colspan="10" class="empty">暂无订单记录</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.orders-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.header-left { display: flex; align-items: center; gap: 1rem; }
.header-left h2 { margin: 0; color: var(--text-primary); }
.btn-back { background: none; border: 1px solid var(--glass-border-strong); color: var(--text-muted); padding: 0.25rem 0.75rem; border-radius: 4px; cursor: pointer; font-size: 0.9rem; }
.btn-primary { padding: 0.5rem 1rem; background: var(--accent-blue); color: var(--text-primary); border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: var(--text-primary); border: 1px solid var(--glass-border-strong); border-radius: 4px; cursor: pointer; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); }
.table th { color: var(--text-muted); font-weight: 600; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.side-buy { color: var(--accent-green); font-weight: 600; }
.side-sell { color: var(--accent-red); font-weight: 600; }
.status-badge { display: inline-block; padding: 0.125rem 0.5rem; border-radius: 999px; color: var(--text-primary); font-size: 0.8rem; font-weight: 600; }
.input { width: 100%; padding: 0.75rem; margin-bottom: 0.75rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-primary); box-sizing: border-box; }
.form-row { display: flex; gap: 0.75rem; }
.form-row select { flex: 1; }
</style>
