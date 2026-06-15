<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { Message } from '@arco-design/web-vue'
import { exchangesApi, type Exchange, type ExchangeOrder } from '../api/exchanges'
import { getExchangeInfo } from '../api/exchangeInfo'
import type { ApiError } from '../api/client'
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
const currentPage = ref(1)
const totalOrders = ref(0)
const pageSize = ref(10)
const filterPair = ref('')
const filterSide = ref<string | undefined>()
const filterOrderType = ref<string | undefined>()
const filterStatus = ref<string | undefined>()

const orderColumns = [
  { title: '交易对', dataIndex: 'pair' },
  { title: '方向', slotName: 'side' },
  { title: '类型', slotName: 'type' },
  { title: '状态', slotName: 'status' },
  { title: '价格', slotName: 'price' },
  { title: '数量', slotName: 'quantity' },
  { title: '已成交', slotName: 'filled' },
  { title: '下单时间', slotName: 'placedAt' },
]

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

async function loadOrders(id: string, label: string, type: 'open' | 'history', page = 1) {
  ordersExchangeId.value = id
  ordersExchangeLabel.value = label
  ordersType.value = type
  currentPage.value = page
  showOrders.value = true
  ordersLoading.value = true
  try {
    const { data } = await exchangesApi.getOrders(id, type, page, pageSize.value,
      filterPair.value || undefined,
      type === 'history' ? filterSide.value : undefined,
      type === 'history' ? filterOrderType.value : undefined,
      type === 'history' ? filterStatus.value : undefined)
    orders.value = data.data ?? []
    totalOrders.value = data.total ?? 0
    currentPage.value = data.page ?? page
  } catch {
    Message.error('获取订单失败')
  } finally {
    ordersLoading.value = false
  }
}

function applyFilters() {
  loadOrders(ordersExchangeId.value, ordersExchangeLabel.value, ordersType.value, 1)
}

function resetFilters() {
  filterPair.value = ''
  filterSide.value = undefined
  filterOrderType.value = undefined
  filterStatus.value = undefined
  loadOrders(ordersExchangeId.value, ordersExchangeLabel.value, ordersType.value, 1)
}

function onPageChange(page: number) {
  loadOrders(ordersExchangeId.value, ordersExchangeLabel.value, ordersType.value, page)
}

function onPageSizeChange(size: number) {
  pageSize.value = size
  currentPage.value = 1
  loadOrders(ordersExchangeId.value, ordersExchangeLabel.value, ordersType.value, 1)
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
  const enabled = accounts.value.filter(a => a.status === 'Enabled')
  if (!enabled.length) return
  assetLoading.value = true
  try {
    const results = await Promise.allSettled(
      enabled.map(a => exchangesApi.getAssets(a.id).then(r => ({ id: a.id, items: r.data })))
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
  formLabel.value = account.name
  formExchangeType.value = account.type
  formApiKey.value = ''
  formSecretKey.value = ''
  formPassphrase.value = ''
  formIsTestnet.value = false
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
  } catch (e: unknown) {
    if ((e as ApiError)._mfaCancelled) return
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
  if (typeof msg === 'object' && msg !== null && 'connected' in msg) {
    return (msg as { connected: boolean }).connected
  }
  if (/失败|错误|异常|无权限|error|invalid|unreachable|denied|reject|timeout|expired|HTTP [45]\d\d/i.test(String(msg))) {
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

    <a-modal v-model:visible="showOrders" :title="`${ordersExchangeLabel} - 订单`" width="1100px" :mask-closable="false">
      <a-tabs v-model:active-key="ordersType" @change="(key) => loadOrders(ordersExchangeId, ordersExchangeLabel, key as 'open' | 'history')">
        <a-tab-pane key="open" title="当前挂单" />
        <a-tab-pane key="history" title="历史订单" />
      </a-tabs>
      <div v-if="ordersType === 'history'" class="filter-bar">
        <a-input v-model="filterPair" placeholder="交易对" style="width:140px" allow-clear @clear="applyFilters" @press-enter="applyFilters" />
        <a-select v-model="filterSide" placeholder="方向" style="width:100px" allow-clear @change="applyFilters">
          <a-option value="Buy">买入</a-option>
          <a-option value="Sell">卖出</a-option>
        </a-select>
        <a-select v-model="filterOrderType" placeholder="类型" style="width:110px" allow-clear @change="applyFilters">
          <a-option value="Market">市价</a-option>
          <a-option value="Limit">限价</a-option>
          <a-option value="StopLimit">止损限价</a-option>
        </a-select>
        <a-select v-model="filterStatus" placeholder="状态" style="width:120px" allow-clear @change="applyFilters">
          <a-option value="New">待成交</a-option>
          <a-option value="PartiallyFilled">部分成交</a-option>
          <a-option value="Filled">已成交</a-option>
          <a-option value="Cancelled">已撤销</a-option>
          <a-option value="Expired">已过期</a-option>
        </a-select>
        <a-button size="small" type="primary" @click="applyFilters">
          <template #icon><icon-search /></template>
          查询
        </a-button>
        <a-button size="small" @click="resetFilters">
          <template #icon><icon-refresh /></template>
          重置
        </a-button>
      </div>
      <a-table
        :columns="orderColumns"
        :data="orders"
        :loading="ordersLoading"
        :pagination="{
          current: currentPage,
          pageSize: pageSize,
          total: totalOrders,
          showTotal: true,
          pageSizeChangeable: true,
          pageSizeOptionValues: [10, 20, 50, 100]
        }"
        row-key="exchangeOrderId"
        @page-change="onPageChange"
        @page-size-change="onPageSizeChange"
      >
        <template #side="{ record }">
          <span :class="record.side === 'Buy' ? 'side-buy' : 'side-sell'">
            {{ record.side === 'Buy' ? '买入' : '卖出' }}
          </span>
        </template>
        <template #type="{ record }">
          {{ record.type === 'Market' ? '市价' : record.type === 'Limit' ? '限价' : '止损限价' }}
        </template>
        <template #status="{ record }">
          <a-tag :color="orderStatusColor(record.status)">
            {{ orderStatusLabels[record.status] ?? record.status }}
          </a-tag>
        </template>
        <template #price="{ record }">
          {{ record.price > 0 ? record.price.toLocaleString() : '-' }}
        </template>
        <template #quantity="{ record }">
          {{ record.quantity.toLocaleString() }}
        </template>
        <template #filled="{ record }">
          {{ record.filledQuantity.toLocaleString() }}
        </template>
        <template #placedAt="{ record }">
          {{ new Date(record.placedAt).toLocaleString('zh-CN', { hour12: false }) }}
        </template>
      </a-table>
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
        :style="{ borderTopColor: getExchangeInfo(a.type).color }"
      >
        <div class="card-header">
          <div class="card-logo">
            <img :src="getExchangeInfo(a.type).icon" :alt="getExchangeInfo(a.type).label" />
          </div>
          <div class="card-title-area">
            <h3>{{ a.name }}</h3>
            <span
              class="exchange-badge"
              :style="{
                background: getExchangeInfo(a.type).bgColor,
                color: getExchangeInfo(a.type).color
              }"
            >
              {{ getExchangeInfo(a.type).label }}
            </span>
          </div>
          <div class="card-header-actions">
            <a-button size="mini" type="text" title="编辑" @click="openEdit(a)">
              <template #icon><icon-edit /></template>
            </a-button>
            <a-switch :model-value="a.status === 'Enabled'" @change="() => toggleStatus(a.id, a.status !== 'Enabled')" />
          </div>
        </div>

        <div class="card-body">
          <div class="info-row">
            <span class="info-label">状态</span>
            <a-tag :color="a.status === 'Enabled' ? 'green' : ''">{{ a.status === 'Enabled' ? '启用' : '禁用' }}</a-tag>
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
            <template v-if="a.status === 'Enabled'">
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
          <a-button size="small" @click="loadOrders(a.id, a.name, 'open')">
            <template #icon><icon-list /></template>
            订单
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
.filter-bar {
  display: flex;
  gap: 0.5rem;
  align-items: center;
  margin-bottom: 0.75rem;
}
</style>
