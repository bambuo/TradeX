<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { ordersApi, type Order } from '../api/orders'
import { exchangesApi, type ExchangeAccount } from '../api/exchanges'

const route = useRoute()
const router = useRouter()
const traderId = route.params.traderId as string

const orders = ref<Order[]>([])
const accounts = ref<ExchangeAccount[]>([])
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
  PartiallyFilled: '#38bdf8',
  Filled: '#22c55e',
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
    accounts.value = accRes.data.data ?? []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  formExchangeId.value = accounts.value[0]?.id ?? ''
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
        <button class="btn-back" @click="router.push(`/traders/${traderId}/positions`)">← 持仓</button>
        <h2>订单记录</h2>
      </div>
      <button class="btn-primary" @click="openCreate">手动下单</button>
    </header>

    <div v-if="showForm" class="modal-overlay" @click.self="showForm = false">
      <div class="modal">
        <h3>手动下单</h3>
        <select v-model="formExchangeId">
          <option v-for="a in accounts" :key="a.id" :value="a.id">
            {{ a.label }} ({{ a.exchangeType }})
          </option>
        </select>
        <input v-model="formSymbolId" placeholder="交易对，如 BTCUSDT" />
        <div class="form-row">
          <select v-model="formSide">
            <option value="Buy">买入</option>
            <option value="Sell">卖出</option>
          </select>
          <select v-model="formType">
            <option value="Market">市价</option>
            <option value="Limit">限价</option>
          </select>
        </div>
        <input v-model.number="formQuantity" type="number" step="0.0001" placeholder="数量" />
        <input v-if="formType === 'Limit'" v-model.number="formPrice" type="number" step="0.01" placeholder="价格" />
        <div class="modal-actions">
          <button class="btn-secondary" @click="showForm = false">取消</button>
          <button class="btn-primary" :disabled="submitting" @click="submitOrder">
            {{ submitting ? '提交中...' : '提交订单' }}
          </button>
        </div>
      </div>
    </div>

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
          <td>{{ o.price ?? '-' }}</td>
          <td>{{ o.quantity }}</td>
          <td>{{ o.filledQuantity }}</td>
          <td>{{ o.fee }} {{ o.feeAsset ?? '' }}</td>
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
.header-left h2 { margin: 0; color: #e2e8f0; }
.btn-back { background: none; border: 1px solid #475569; color: #94a3b8; padding: 0.25rem 0.75rem; border-radius: 4px; cursor: pointer; font-size: 0.9rem; }
.btn-primary { padding: 0.5rem 1rem; background: #38bdf8; color: #0f172a; border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; }
.table th { color: #94a3b8; font-weight: 600; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.side-buy { color: #22c55e; font-weight: 600; }
.side-sell { color: #ef4444; font-weight: 600; }
.status-badge { display: inline-block; padding: 0.125rem 0.5rem; border-radius: 999px; color: #0f172a; font-size: 0.8rem; font-weight: 600; }
.modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; justify-content: center; align-items: center; z-index: 100; }
.modal { background: #1e293b; padding: 2rem; border-radius: 8px; width: 100%; max-width: 400px; }
.modal h3 { margin: 0 0 1rem; color: #e2e8f0; }
.modal input, .modal select { width: 100%; padding: 0.75rem; margin-bottom: 0.75rem; border: 1px solid #334155; border-radius: 4px; background: #0f172a; color: #e2e8f0; box-sizing: border-box; }
.form-row { display: flex; gap: 0.75rem; }
.form-row select { flex: 1; }
.modal-actions { display: flex; justify-content: flex-end; gap: 0.5rem; margin-top: 0.5rem; }
</style>
