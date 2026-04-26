<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { exchangesApi, type ExchangeAccount as Exchange, type ExchangeOrder } from '../api/exchanges'
import { getExchangeInfo } from '../api/exchangeInfo'
import ExchangeTypeSelect from '../components/ExchangeTypeSelect.vue'

const accounts = ref<Exchange[]>([])
const loading = ref(true)
const assets = ref<Record<string, { currency: string; balance: number }[]>>({})
const assetLoading = ref(false)
const expandedAssets = ref<Record<string, boolean>>({})
const showForm = ref(false)
const editId = ref<string | null>(null)
const testingId = ref<string | null>(null)
const testResult = ref<{ id: string; connected: boolean; error?: string } | null>(null)
const toast = ref({ message: '', type: '' as 'success' | 'error' })
const showOrders = ref(false)
const ordersLoading = ref(false)
const orders = ref<ExchangeOrder[]>([])
const ordersExchangeLabel = ref('')
const ordersExchangeId = ref('')
const ordersType = ref<'open' | 'history'>('open')

const orderStatusLabels: Record<string, string> = {
  New: '待成交',
  PartiallyFilled: '部分成交',
  Filled: '已成交',
  Cancelled: '已撤销',
  Expired: '已过期'
}

const orderStatusColor = computed(() => (status: string) => {
  const colors: Record<string, string> = {
    New: '#f59e0b',
    PartiallyFilled: 'var(--accent-blue)',
    Filled: 'var(--accent-green)',
    Cancelled: '#64748b',
    Expired: '#64748b'
  }
  return colors[status] ?? '#64748b'
})

async function loadOrders(id: string, label: string, type: 'open' | 'history') {
  ordersExchangeId.value = id
  ordersExchangeLabel.value = label
  ordersType.value = type
  orders.value = []
  showOrders.value = true
  ordersLoading.value = true
  try {
    const { data } = await exchangesApi.getOrders(id, type)
    orders.value = data.data ?? []
  } catch {
    showToast('获取订单失败', 'error')
  } finally {
    ordersLoading.value = false
  }
}

function showToast(message: string, type: 'success' | 'error') {
  toast.value = { message, type }
  setTimeout(() => { toast.value.message = '' }, 4000)
}

const formLabel = ref('')
const formExchangeType = ref('Binance')
const formApiKey = ref('')
const formSecretKey = ref('')
const formPassphrase = ref('')
const formIsTestnet = ref(false)

async function loadAll() {
  loading.value = true
  try {
    const { data } = await exchangesApi.getAll()
    accounts.value = data.data ?? []
  } finally {
    loading.value = false
  }
  await fetchAssets()
}

async function fetchAssets(exchangeId?: string) {
  if (exchangeId) {
    try {
      const { data } = await exchangesApi.getAssets(exchangeId)
      assets.value[exchangeId] = data.data ?? []
    } catch {
      showToast('获取资产失败', 'error')
    }
    return
  }
  const enabled = accounts.value.filter(a => a.isEnabled)
  if (!enabled.length) return
  assetLoading.value = true
  try {
    const results = await Promise.allSettled(
      enabled.map(a => exchangesApi.getAssets(a.id).then(r => ({ id: a.id, items: r.data.data })))
    )
    for (const r of results) {
      if (r.status === 'fulfilled') {
        assets.value[r.value.id] = r.value.items ?? []
      }
    }
  } finally {
    assetLoading.value = false
  }
}

function openCreate() {
  editId.value = null
  formLabel.value = ''
  formExchangeType.value = 'Binance'
  formApiKey.value = ''
  formSecretKey.value = ''
  formPassphrase.value = ''
  formIsTestnet.value = false
  showForm.value = true
}

function openEdit(account: Exchange) {
  editId.value = account.id
  formLabel.value = account.label
  formExchangeType.value = account.exchangeType
  formApiKey.value = ''
  formSecretKey.value = ''
  formPassphrase.value = ''
  formIsTestnet.value = account.isTestnet
  showForm.value = true
}

async function save() {
  if (editId.value) {
    const payload: Record<string, unknown> = { name: formLabel.value }
    if (formApiKey.value) payload.apiKey = formApiKey.value
    if (formSecretKey.value) payload.secretKey = formSecretKey.value
    if (formPassphrase.value) payload.passphrase = formPassphrase.value
    await exchangesApi.update(editId.value, payload)
  } else {
    await exchangesApi.create({
      name: formLabel.value,
      exchangeType: formExchangeType.value,
      apiKey: formApiKey.value,
      secretKey: formSecretKey.value,
      passphrase: formPassphrase.value || undefined,
      isTestnet: formIsTestnet.value
    })
  }
  showForm.value = false
  await loadAll()
}

async function toggleStatus(id: string) {
  await exchangesApi.toggleStatus(id)
  await loadAll()
}

function getTestOk(account: Exchange): boolean | null {
  if (testResult.value?.id === account.id) {
    return testResult.value.connected
  }
  if (!account.testResult) return null
  const msg = account.testResult
  if (/失败|错误|异常|无权限|error|invalid|unreachable|denied|reject|timeout|expired|HTTP \d/i.test(msg)) {
    return false
  }
  return true
}

async function testConnection(id: string) {
  testingId.value = id
  testResult.value = null
  try {
    const { data } = await exchangesApi.testConnection(id)
    testResult.value = { id, ...data }
    if (data.connected) {
      showToast('连接测试成功', 'success')
    } else {
      showToast(`连接失败${data.error ? ': ' + data.error : ''}`, 'error')
    }
    await loadAll()
  } catch {
    testResult.value = { id, connected: false, error: '请求失败' }
    showToast('请求失败', 'error')
  } finally {
    testingId.value = null
    setTimeout(() => { testResult.value = null }, 8000)
  }
}

onMounted(loadAll)
</script>

<template>
  <div class="exchanges-page">
    <header class="page-header">
      <h2>交易所管理</h2>
      <AppButton variant="primary" icon="plus" @click="openCreate">添加交易所</AppButton>
    </header>

    <Transition name="toast-fade">
      <div v-if="toast.message" class="toast" :class="`toast--${toast.type}`">
        {{ toast.message }}
      </div>
    </Transition>

    <AppModal v-model="showForm" :title="editId ? '编辑交易所' : '添加交易所'" width="sm">
      <div class="form-body">
        <input v-model="formLabel" placeholder="名称（如：币安主账户）" class="input" />
        <ExchangeTypeSelect v-model="formExchangeType" :disabled="!!editId" />
        <input v-model="formApiKey" :placeholder="editId ? 'API Key（留空则不修改）' : 'API Key'" type="password" class="input" />
        <input v-model="formSecretKey" :placeholder="editId ? 'Secret Key（留空则不修改）' : 'Secret Key'" type="password" class="input" />
        <input v-model="formPassphrase" :placeholder="editId ? 'Passphrase（留空则不修改）' : 'Passphrase（选填）'" type="password" class="input" />
        <label class="checkbox-label">
          <input v-model="formIsTestnet" type="checkbox" />
          测试网
        </label>
      </div>
      <template #footer>
        <AppButton icon="close" @click="showForm = false">取消</AppButton>
        <AppButton variant="primary" icon="save" @click="save">保存</AppButton>
      </template>
    </AppModal>

    <AppModal v-model="showOrders" :title="`${ordersExchangeLabel}${ordersType === 'open' ? ' - 当前挂单' : ' - 历史订单'}`" width="lg">
      <div class="modal-tabs">
        <button class="modal-tab" :class="{ active: ordersType === 'open' }" @click="loadOrders(ordersExchangeId, ordersExchangeLabel, 'open')">当前挂单</button>
        <button class="modal-tab" :class="{ active: ordersType === 'history' }" @click="loadOrders(ordersExchangeId, ordersExchangeLabel, 'history')">历史订单</button>
      </div>
      <div v-if="ordersLoading" class="loading">加载中...</div>
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
            <th>下单时间</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="o in orders" :key="o.exchangeOrderId">
            <td>{{ o.symbol }}</td>
            <td>
              <span :class="o.side === 'Buy' ? 'side-buy' : 'side-sell'">
                {{ o.side === 'Buy' ? '买入' : '卖出' }}
              </span>
            </td>
            <td>{{ o.type === 'Market' ? '市价' : o.type === 'Limit' ? '限价' : '止损限价' }}</td>
            <td>
              <span class="order-status-badge" :style="{ background: orderStatusColor(o.status) }">
                {{ orderStatusLabels[o.status] ?? o.status }}
              </span>
            </td>
            <td>{{ o.price > 0 ? o.price.toLocaleString() : '-' }}</td>
            <td>{{ o.quantity.toLocaleString() }}</td>
            <td>{{ o.filledQuantity.toLocaleString() }}</td>
            <td>{{ new Date(o.placedAt).toLocaleString('zh-CN', { hour12: false }) }}</td>
          </tr>
          <tr v-if="!ordersLoading && orders.length === 0">
            <td colspan="8" class="empty">{{ ordersType === 'open' ? '暂无挂单' : '暂无历史订单' }}</td>
          </tr>
        </tbody>
      </table>
      <template #footer>
        <AppButton icon="close" @click="showOrders = false">关闭</AppButton>
      </template>
    </AppModal>

    <div v-if="loading" class="loading">加载中...</div>

    <div v-else-if="accounts.length === 0" class="empty">暂无交易所</div>

    <div v-else class="card-grid">
      <div
        v-for="a in accounts"
        :key="a.id"
        class="exchange-card"
        :style="{ borderTopColor: getExchangeInfo(a.exchangeType).color }"
      >
        <div class="card-header">
          <div class="card-logo">
            <img :src="getExchangeInfo(a.exchangeType).svgUrl" :alt="getExchangeInfo(a.exchangeType).label" />
          </div>
          <div class="card-title-area">
            <h3>{{ a.label }}</h3>
            <span
              class="exchange-badge"
              :style="{
                background: getExchangeInfo(a.exchangeType).bgColor,
                color: getExchangeInfo(a.exchangeType).color
              }"
            >
              {{ getExchangeInfo(a.exchangeType).label }}
            </span>
          </div>
          <div class="card-header-actions">
            <AppButton size="sm" variant="ghost" icon="edit" title="编辑" @click="openEdit(a)" />
            <label class="switch" :title="a.isEnabled ? '禁用' : '启用'">
              <input type="checkbox" :checked="a.isEnabled" @change="toggleStatus(a.id)" />
              <span class="switch-slider" />
            </label>
          </div>
        </div>

        <div class="card-body">
          <div class="info-row">
            <span class="info-label">状态</span>
            <span class="status-badge" :class="a.isEnabled ? 'enabled' : 'disabled'">
              <span class="status-dot" />
              {{ a.isEnabled ? '启用' : '禁用' }}
            </span>
          </div>
          <div class="info-row">
            <span class="info-label">模式</span>
            <span class="mode-badge" :class="a.isTestnet ? 'testnet' : 'mainnet'">
              {{ a.isTestnet ? '测试网' : '主网' }}
            </span>
          </div>
          <div class="info-row">
            <span class="info-label">测试结果</span>
            <span class="info-value">
              <span v-if="testingId === a.id" class="test-loading">
                <span class="spinner" />
              </span>
              <span v-else-if="getTestOk(a) === true" class="test-icon test-icon--ok" title="连接正常">
                <AppIcon name="check" :size="14" />
              </span>
              <span v-else-if="getTestOk(a) === false" class="test-icon test-icon--fail" :title="a.testResult || '连接异常'">
                <AppIcon name="close" :size="14" />
              </span>
              <span v-else class="text-muted">-</span>
            </span>
          </div>
          <div class="info-row">
            <span class="info-label">资产</span>
            <template v-if="a.isEnabled">
              <span v-if="assets[a.id]" class="assets-summary" @click="expandedAssets[a.id] = !expandedAssets[a.id]">
                {{ assets[a.id].length }} 个币种
                <span class="expand-arrow" :class="{ expanded: expandedAssets[a.id] }">▶</span>
              </span>
              <span v-else-if="assetLoading" class="balance-loading">加载中...</span>
              <AppButton v-else size="sm" variant="ghost" @click="fetchAssets(a.id)">加载</AppButton>
            </template>
            <span v-else class="info-value">-</span>
          </div>
          <div v-if="expandedAssets[a.id] && assets[a.id]" class="asset-list">
            <div v-for="item in assets[a.id]" :key="item.currency" class="asset-row">
              <span class="asset-currency">{{ item.currency }}</span>
              <span class="asset-amount">{{ item.balance.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 8 }) }}</span>
            </div>
          </div>
        </div>

        <div class="card-footer">
          <AppButton
            size="sm"
            icon="test"
            :disabled="testingId === a.id"
            @click="testConnection(a.id)"
          >
            测试
          </AppButton>
          <AppButton size="sm" icon="table" @click="loadOrders(a.id, a.label, 'open')">挂单</AppButton>
          <AppButton size="sm" icon="orders" @click="loadOrders(a.id, a.label, 'history')">历史</AppButton>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.exchanges-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; }
.page-header h2 { margin: 0; color: var(--text-primary); }

.loading { text-align: center; color: var(--text-muted); padding: 3rem; font-size: 0.95rem; }
.empty { text-align: center; color: var(--text-muted); padding: 3rem; font-size: 0.95rem; }

/* Toast */
.toast {
  position: fixed;
  top: 1rem;
  left: 50%;
  transform: translateX(-50%);
  z-index: 1200;
  padding: 0.75rem 1.5rem;
  font-size: 0.85rem;
  backdrop-filter: blur(24px) saturate(180%);
  -webkit-backdrop-filter: blur(24px) saturate(180%);
  box-shadow: 0 18px 50px rgba(2, 6, 23, 0.28);
  pointer-events: none;
  border-radius: 6px;
}
.toast--success {
  background: rgba(21, 128, 61, 0.85);
  border: 1px solid rgba(34, 197, 94, 0.5);
  color: #fff;
}
.toast--error {
  background: rgba(185, 28, 28, 0.85);
  border: 1px solid rgba(239, 68, 68, 0.5);
  color: #fff;
}
.toast-fade-enter-active, .toast-fade-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}
.toast-fade-enter-from, .toast-fade-leave-to {
  opacity: 0;
  transform: translateY(-0.5rem);
}

/* Card grid */
.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
  gap: 1rem;
  align-items: start;
}

.exchange-card {
  background: var(--card-bg, #fff);
  border: 1px solid var(--glass-border);
  border-top: 3px solid;
  border-radius: 8px;
  overflow: hidden;
  transition: box-shadow 0.2s ease, transform 0.2s ease;
}
.exchange-card:hover {
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.06);
  transform: translateY(-2px);
}

.card-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem 1.25rem 0.5rem;
}

.card-logo {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.card-logo img {
  width: 36px;
  height: 36px;
}

.card-title-area {
  flex: 1;
  min-width: 0;
}
.card-title-area h3 {
  margin: 0 0 0.25rem;
  font-size: 1rem;
  color: var(--text-primary);
  font-weight: 600;
  line-height: 1.3;
}

.card-header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  align-self: flex-start;
}

.switch {
  position: relative;
  display: inline-block;
  width: 36px;
  height: 20px;
  cursor: pointer;
}
.switch input {
  display: none;
}
.switch-slider {
  position: absolute;
  inset: 0;
  background: var(--glass-border-strong);
  border-radius: 999px;
  transition: background 0.2s ease;
}
.switch-slider::before {
  content: '';
  position: absolute;
  width: 16px;
  height: 16px;
  left: 2px;
  bottom: 2px;
  background: #fff;
  border-radius: 50%;
  transition: transform 0.2s ease;
}
.switch input:checked + .switch-slider {
  background: #4ade80;
}
.switch input:checked + .switch-slider::before {
  transform: translateX(16px);
}

.exchange-badge {
  display: inline-block;
  padding: 0.1rem 0.5rem;
  border-radius: 4px;
  font-size: 0.72rem;
  font-weight: 600;
  line-height: 1.5;
}

.card-body {
  padding: 0.5rem 1.25rem 0.75rem;
}

.info-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.3rem 0;
  font-size: 0.85rem;
}

.info-label {
  color: var(--text-muted);
  flex-shrink: 0;
}

.info-value {
  color: var(--text-primary);
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.4rem;
}

.status-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.35rem;
  padding: 0.1rem 0.5rem;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 500;
}
.status-badge.enabled {
  background: rgba(34, 197, 94, 0.10);
  color: #4ade80;
}
.status-badge.disabled {
  background: rgba(148, 163, 184, 0.12);
  color: #94a3b8;
}
.status-dot {
  width: 0.4rem;
  height: 0.4rem;
  border-radius: 50%;
  background: currentColor;
  flex-shrink: 0;
}

.mode-badge {
  display: inline-block;
  padding: 0.1rem 0.5rem;
  border-radius: 4px;
  font-size: 0.78rem;
  font-weight: 500;
}
.mode-badge.mainnet {
  background: rgba(59, 130, 246, 0.10);
  color: #60a5fa;
}
.mode-badge.testnet {
  background: rgba(234, 179, 8, 0.10);
  color: #eab308;
}

.text-muted { color: var(--text-muted); }

.test-icon {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
}
.test-icon--ok {
  color: #4ade80;
}
.test-icon--fail {
  color: #f87171;
}
.test-loading {
  display: inline-flex;
  align-items: center;
}
.spinner {
  width: 14px;
  height: 14px;
  border: 2px solid var(--glass-border-strong);
  border-top-color: var(--accent-blue);
  border-radius: 50%;
  animation: spin 0.6s linear infinite;
}
@keyframes spin {
  to { transform: rotate(360deg); }
}

.balance-value {
  color: var(--accent-green);
  font-weight: 600;
  font-size: 0.88rem;
}
.balance-loading {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.assets-summary {
  cursor: pointer;
  color: var(--accent-blue);
  font-size: 0.85rem;
  font-weight: 500;
  display: flex;
  align-items: center;
  gap: 0.3rem;
  user-select: none;
}
.assets-summary:hover {
  opacity: 0.8;
}

.expand-arrow {
  font-size: 0.6rem;
  transition: transform 0.2s ease;
  display: inline-block;
}
.expand-arrow.expanded {
  transform: rotate(90deg);
}

.asset-list {
  border-top: 1px solid var(--glass-border);
  margin-top: 0.5rem;
  padding-top: 0.4rem;
}
.asset-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 0.2rem 0;
  font-size: 0.82rem;
}
.asset-currency {
  color: var(--text-primary);
  font-weight: 500;
}
.asset-amount {
  color: var(--text-muted);
  font-variant-numeric: tabular-nums;
}

.card-footer {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem 1.25rem 1rem;
  border-top: 1px solid var(--glass-border);
}
.card-footer :deep(.app-button) {
  flex: 1;
}

.form-body {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
.form-body .input,
.form-body .checkbox-label {
  margin-bottom: 0;
}
.form-body :deep(.exchange-select) {
  margin-bottom: 0;
}

.input { width: 100%; padding: 0.75rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-primary); box-sizing: border-box; }
.input:is(select) { cursor: pointer; }
.checkbox-label { display: flex; align-items: center; gap: 0.5rem; color: var(--text-primary); cursor: pointer; }
.checkbox-label input { width: auto; margin: 0; }

.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); }
.table th { color: var(--text-muted); font-weight: 600; }
.side-buy { color: var(--accent-green); font-weight: 600; }
.side-sell { color: var(--accent-red); font-weight: 600; }
.order-status-badge { display: inline-block; padding: 0.125rem 0.5rem; border-radius: 999px; color: #fff; font-size: 0.8rem; font-weight: 600; }
.modal-tabs { display: flex; gap: 0; margin-bottom: 1rem; border-bottom: 1px solid var(--glass-border); }
.modal-tab { padding: 0.5rem 1rem; border: none; background: none; color: var(--text-muted); cursor: pointer; font-size: 0.88rem; border-bottom: 2px solid transparent; transition: all 0.15s ease; }
.modal-tab:hover { color: var(--text-primary); }
.modal-tab.active { color: var(--accent-blue); border-bottom-color: var(--accent-blue); }
</style>
