<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { Message } from '@arco-design/web-vue'
import { exchangesApi, type Exchange, type ExchangeOrder } from '../api/exchanges'
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
const showOrders = ref(false)
const ordersLoading = ref(false)
const orders = ref<ExchangeOrder[]>([])
const ordersExchangeLabel = ref('')
const ordersExchangeId = ref('')
const ordersType = ref<'open' | 'history'>('open')

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

const orderStatusLabels: Record<string, string> = {
  New: '待成交',
  PartiallyFilled: '部分成交',
  Filled: '已成交',
  Cancelled: '已撤销',
  Expired: '已过期'
}

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
    Message.error('获取订单失败')
  } finally {
    ordersLoading.value = false
  }
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
      Message.error('获取资产失败')
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
  try {
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
  } catch (e: any) {
    if (e._mfaCancelled) return
    throw e
  }
}

async function toggleStatus(id: string, enable: boolean) {
  try {
    await exchangesApi.toggleStatus(id, enable)
    await loadAll()
  } catch {
    Message.error('切换失败')
  }
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
      Message.success('连接测试成功')
    } else {
      Message.error(`连接失败${data.error ? ': ' + data.error : ''}`)
    }
    await loadAll()
  } catch {
    testResult.value = { id, connected: false, error: '请求失败' }
    Message.error('请求失败')
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
      <a-button type="primary" @click="openCreate">
        <template #icon><icon-plus /></template>
        添加交易所
      </a-button>
    </header>

    <a-modal v-model:visible="showForm" :title="editId ? '编辑交易所' : '添加交易所'" width="sm" :mask-closable="false">
      <div class="form-body">
        <a-input v-model="formLabel" placeholder="名称（如：币安主账户）" />
        <ExchangeTypeSelect v-model="formExchangeType" :disabled="!!editId" />
        <a-input-password v-model="formApiKey" :placeholder="editId ? 'API Key（留空则不修改）' : 'API Key'" />
        <a-input-password v-model="formSecretKey" :placeholder="editId ? 'Secret Key（留空则不修改）' : 'Secret Key'" />
        <a-input-password v-model="formPassphrase" :placeholder="editId ? 'Passphrase（留空则不修改）' : 'Passphrase（选填）'" />
        <a-checkbox v-model="formIsTestnet">测试网</a-checkbox>
      </div>
      <template #footer>
        <a-button type="primary" @click="save">
          <template #icon><icon-save /></template>
          保存
        </a-button>
      </template>
    </a-modal>

    <a-modal v-model:visible="showOrders" :title="`${ordersExchangeLabel}${ordersType === 'open' ? ' - 当前挂单' : ' - 历史订单'}`" width="1100px" :mask-closable="false">
      <a-tabs v-model:active-key="ordersType" @change="(key) => loadOrders(ordersExchangeId, ordersExchangeLabel, key as 'open' | 'history')">
        <a-tab-pane key="open" title="当前挂单" />
        <a-tab-pane key="history" title="历史订单" />
      </a-tabs>
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
            <td>{{ o.pair }}</td>
            <td>
              <span :class="o.side === 'Buy' ? 'side-buy' : 'side-sell'">
                {{ o.side === 'Buy' ? '买入' : '卖出' }}
              </span>
            </td>
            <td>{{ o.type === 'Market' ? '市价' : o.type === 'Limit' ? '限价' : '止损限价' }}</td>
            <td>
              <a-tag :color="orderStatusColor(o.status)">
                {{ orderStatusLabels[o.status] ?? o.status }}
              </a-tag>
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
        <a-button @click="showOrders = false">
          <template #icon><icon-close /></template>
          关闭
        </a-button>
      </template>
    </a-modal>

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
            <img :src="getExchangeInfo(a.exchangeType).icon" :alt="getExchangeInfo(a.exchangeType).label" />
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
            <a-button size="mini" type="text" title="编辑" @click="openEdit(a)">
              <template #icon><icon-edit /></template>
            </a-button>
            <a-switch :model-value="a.isEnabled" @change="() => toggleStatus(a.id, !a.isEnabled)" />
          </div>
        </div>

        <div class="card-body">
          <div class="info-row">
            <span class="info-label">状态</span>
            <a-tag :color="a.isEnabled ? 'green' : ''">{{ a.isEnabled ? '启用' : '禁用' }}</a-tag>
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
                <a-spin :size="14" />
              </span>
              <span v-else-if="getTestOk(a) === true" class="test-icon test-icon--ok" title="连接正常">
                <icon-check :size="14" />
              </span>
              <span v-else-if="getTestOk(a) === false" class="test-icon test-icon--fail" :title="a.testResult || '连接异常'">
                <icon-close :size="14" />
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
              <a-button v-else size="mini" type="text" @click="fetchAssets(a.id)">加载</a-button>
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
          <a-button size="small" :loading="testingId === a.id" @click="testConnection(a.id)">
            <template #icon><icon-check-circle /></template>
            测试
          </a-button>
          <a-button size="small" @click="loadOrders(a.id, a.label, 'open')">
            <template #icon><icon-list /></template>
            挂单
          </a-button>
          <a-button size="small" @click="loadOrders(a.id, a.label, 'history')">
            <template #icon><icon-history /></template>
            历史
          </a-button>
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
.card-footer :deep(.arco-btn) {
  flex: 1;
}

.form-body {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); }
.table th { color: var(--text-muted); font-weight: 600; }
.side-buy { color: var(--accent-green); font-weight: 600; }
.side-sell { color: var(--accent-red); font-weight: 600; }
</style>
