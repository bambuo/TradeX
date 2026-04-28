<script setup lang="ts">
import { ref, onMounted, watch, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { strategiesApi, type Strategy, type StrategyDeployment } from '../api/strategies'
import { exchangesApi, type Exchange } from '../api/exchanges'
import { formatSmallNumber } from '../utils/format'


const route = useRoute()
const router = useRouter()
const traderId = route.params.traderId as string

const deployments = ref<StrategyDeployment[]>([])
const templates = ref<Strategy[]>([])
const exchanges = ref<Exchange[]>([])
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const toggleLoading = ref<string | null>(null)

const page = ref(1)
const pageSize = ref(15)

const scopeTagColors: Record<string, string> = {
  Trader: '', Exchange: 'green', Symbol: 'blue'
}

const columns = [
  { title: '作用域', dataIndex: 'scope', slotName: 'scope', width: 90 },
  { title: '策略', dataIndex: 'strategyName', width: 180, ellipsis: true },
  { title: '交易所', dataIndex: 'exchangeName', width: 130, ellipsis: true },
  { title: '交易对', dataIndex: 'symbolList', width: 170, ellipsis: true },
  { title: '周期', dataIndex: 'timeframe', width: 60 },
  { title: '状态', dataIndex: 'status', slotName: 'status', width: 80 },
  { title: '更新时间', dataIndex: 'updatedAt', width: 160 },
  { title: '操作', dataIndex: 'actions', slotName: 'actions', width: 340 }
]

const scope = ref<'Trader' | 'Exchange' | 'Symbol'>('Symbol')
const formStrategyId = ref(route.query.strategyId as string || '')
const formExchangeId = ref('')
const formSymbolIds = ref<string[]>([])
const formTimeframe = ref('15m')

const symbols = ref<{ symbol: string; pricePrecision: number; price: number; priceChangePercent: number; volume: number; highPrice: number; lowPrice: number }[]>([])
const symbolsLoading = ref(false)
const symbolSearch = ref('')
const sortKey = ref<'symbol' | 'price' | 'priceChangePercent' | 'volume'>('volume')
const sortDesc = ref(true)
const priceMin = ref<number | null>(null)
const priceMax = ref<number | null>(null)
const changeDirection = ref<'all' | 'up' | 'down'>('all')
const volMin = ref<number | null>(null)

const timeframes = ['1m', '5m', '15m', '30m', '1h', '4h', '1d']

const scopeLabels: Record<string, string> = {
  Trader: '交易员级',
  Exchange: '交易所级',
  Symbol: '交易对级'
}

const scopeDescriptions: Record<string, string> = {
  Trader: '全局作用于整个交易员账号，不绑定交易所和交易对',
  Exchange: '仅针对选定交易所生效，覆盖交易员级配置',
  Symbol: '对选定的交易对执行精确策略，优先级最高'
}

const statusLabels: Record<string, string> = {
  Draft: '草稿', Backtesting: '回测中', Passed: '已通过', Active: '活跃', Disabled: '已禁用'
}

const tagStatusColors: Record<string, string> = {
  Draft: '', Backtesting: 'orange', Passed: 'green', Active: 'blue', Disabled: 'red'
}

const statusByCode: Record<number, string> = {
  0: 'Draft',
  1: 'Backtesting',
  2: 'Passed',
  3: 'Active',
  4: 'Disabled'
}

const sortedSymbols = computed(() => {
  let list = symbols.value
  if (symbolSearch.value) {
    const q = symbolSearch.value.toLowerCase()
    list = list.filter(s => s.symbol.toLowerCase().includes(q))
  }
  if (priceMin.value != null && !isNaN(priceMin.value)) list = list.filter(s => s.price >= priceMin.value!)
  if (priceMax.value != null && !isNaN(priceMax.value)) list = list.filter(s => s.price <= priceMax.value!)
  if (changeDirection.value === 'up') list = list.filter(s => s.priceChangePercent > 0)
  else if (changeDirection.value === 'down') list = list.filter(s => s.priceChangePercent < 0)
  if (volMin.value != null && !isNaN(volMin.value)) list = list.filter(s => s.volume >= volMin.value!)
  const sorted = [...list]
  sorted.sort((a, b) => {
    const dir = sortDesc.value ? -1 : 1
    if (sortKey.value === 'symbol') return dir * a.symbol.localeCompare(b.symbol)
    return dir * ((a[sortKey.value] ?? 0) - (b[sortKey.value] ?? 0))
  })
  return sorted
})

function toggleSort(key: typeof sortKey.value) {
  if (sortKey.value === key) sortDesc.value = !sortDesc.value
  else { sortKey.value = key; sortDesc.value = true }
}

function sortArrow(key: typeof sortKey.value): string {
  if (sortKey.value !== key) return ''
  return sortDesc.value ? ' ▼' : ' ▲'
}

function normalizeStatus(status: unknown): string {
  if (typeof status === 'number') return statusByCode[status] ?? String(status)
  if (typeof status === 'string' && /^\d+$/.test(status)) {
    const code = Number(status)
    return statusByCode[code] ?? status
  }
  return String(status ?? '')
}

function getStatusLabel(status: unknown): string {
  const normalized = normalizeStatus(status)
  if (statusLabels[normalized]) return statusLabels[normalized]
  return normalized || '-'
}

function isActive(status: unknown): boolean {
  return normalizeStatus(status) === 'Active'
}


async function load() {
  loading.value = true
  try {
    const [depRes, tmplRes, accRes] = await Promise.all([
      strategiesApi.getAll(traderId),
      strategiesApi.getAllPure(),
      exchangesApi.getAll()
    ])
    deployments.value = depRes.data ?? []
    templates.value = tmplRes.data.data ?? []
    exchanges.value = accRes.data.data ?? []
  } finally {
    loading.value = false
  }
}

function getExchangeLabel(exchangeId: string): string {
  return exchanges.value.find(a => a.id === exchangeId)?.label ?? exchangeId
}

function getTemplateName(strategyId: string): string {
  return templates.value.find(t => t.id === strategyId)?.name ?? strategyId
}

function formatPrice(price: number): string {
  if (price === 0) return '-'
  return formatSmallNumber(price)
}

function formatVolume(vol: number): string {
  if (vol === 0) return '-'
  if (vol >= 1_000_000) return (vol / 1_000_000).toFixed(1) + 'M'
  if (vol >= 1_000) return (vol / 1_000).toFixed(1) + 'K'
  return vol.toFixed(0)
}

function renderSymbol(sym: string): { base: string; quote: string } {
  if (sym.endsWith('USDT')) return { base: sym.slice(0, -4), quote: 'USDT' }
  const m = sym.match(/^([A-Za-z]+)(BTC|ETH|BNB|USDC|DAI)$/)
  if (m) return { base: m[1], quote: m[2] }
  return { base: sym, quote: '' }
}

const displayDeployments = computed(() => {
  const start = (page.value - 1) * pageSize.value
  const list = deployments.value.map(d => ({
    ...d,
    key: d.id,
    actions: '',
    strategyName: getTemplateName(d.strategyId),
    exchangeName: d.exchangeId && d.exchangeId !== '00000000-0000-0000-0000-000000000000' ? getExchangeLabel(d.exchangeId) : '-',
    symbolList: d.scope === 'Symbol' ? parseSymbolIds(d.symbolIds).join(', ') : '-'
  }))
  return list.slice(start, start + pageSize.value)
})

async function fetchSymbols(exchangeId: string) {
  if (!exchangeId || exchangeId === '{}') return
  symbolsLoading.value = true
  symbols.value = []
  try {
    const { data } = await exchangesApi.getSymbols(exchangeId)
    symbols.value = data.data ?? []
  } catch {
    symbols.value = []
  } finally {
    symbolsLoading.value = false
  }
}

watch(formExchangeId, (val) => {
  if (scope.value === 'Symbol' && val) fetchSymbols(val)
})

function openCreate() {
  editId.value = null
  scope.value = 'Symbol'
  formStrategyId.value = templates.value[0]?.id ?? ''
  formExchangeId.value = exchanges.value[0]?.id ?? ''
  formSymbolIds.value = []
  formTimeframe.value = '15m'
  symbolSearch.value = ''
  showForm.value = true
}

function openEdit(d: StrategyDeployment) {
  editId.value = d.id
  scope.value = (d.scope as any) || 'Symbol'
  formStrategyId.value = d.strategyId
  formExchangeId.value = d.exchangeId
  formSymbolIds.value = parseSymbolIds(d.symbolIds)
  formTimeframe.value = d.timeframe
  symbolSearch.value = ''
  if (scope.value === 'Symbol' && d.exchangeId && d.exchangeId !== '00000000-0000-0000-0000-000000000000') {
    fetchSymbols(d.exchangeId)
  }
  showForm.value = true
}

function parseSymbolIds(val: string): string[] {
  try {
    const parsed = JSON.parse(val)
    if (Array.isArray(parsed)) return parsed
  } catch {}
  return val.split(',').map(s => s.trim()).filter(Boolean)
}

function toggleSymbol(symbol: string) {
  const idx = formSymbolIds.value.indexOf(symbol)
  if (idx >= 0) formSymbolIds.value.splice(idx, 1)
  else formSymbolIds.value.push(symbol)
}

function serializeSymbolIds(): string {
  if (scope.value !== 'Symbol') return '[]'
  return JSON.stringify(formSymbolIds.value)
}

function swapScope(newScope: 'Trader' | 'Exchange' | 'Symbol') {
  scope.value = newScope
  if (newScope === 'Trader') {
    formExchangeId.value = ''
    formSymbolIds.value = []
  } else if (newScope === 'Exchange') {
    formExchangeId.value = exchanges.value[0]?.id ?? ''
    formSymbolIds.value = []
  } else {
    formExchangeId.value = (formExchangeId.value || exchanges.value[0]?.id) ?? ''
    formSymbolIds.value = []
    if (formExchangeId.value) fetchSymbols(formExchangeId.value)
  }
}

async function save() {
  const symbolIdsStr = serializeSymbolIds()
  if (editId.value) {
    await strategiesApi.update(traderId, editId.value, {
      symbolIds: symbolIdsStr,
      timeframe: formTimeframe.value
    })
  } else {
    await strategiesApi.create(traderId, {
      strategyId: formStrategyId.value,
      exchangeId: ['Exchange', 'Symbol'].includes(scope.value) ? formExchangeId.value : '00000000-0000-0000-0000-000000000000',
      symbolIds: symbolIdsStr,
      timeframe: formTimeframe.value
    })
  }
  showForm.value = false
  await load()
}

async function remove(id: string) {
  await strategiesApi.delete(traderId, id)
  await load()
}

const errorMsg = ref('')

async function toggle(d: StrategyDeployment) {
  toggleLoading.value = d.id
  errorMsg.value = ''
  try {
    await strategiesApi.toggle(traderId, d.id, !isActive(d.status))
    await load()
  } catch (e: any) {
    errorMsg.value = e.response?.data?.message || e.response?.data?.error || '操作失败'
  } finally {
    toggleLoading.value = null
  }
}

onMounted(load)
</script>

<template>
  <div class="strategy-page">
    <a-card class="header-card">
      <div class="header-row">
        <div class="header-left">
          <a-button type="text" size="small" @click="router.push('/traders')">
            <template #icon><icon-left /></template>
            返回
          </a-button>
          <span class="header-title">策略部署</span>
        </div>
        <a-button type="primary" @click="openCreate">
          <template #icon><icon-plus /></template>
          新建部署
        </a-button>
      </div>
    </a-card>

    <a-modal v-model:visible="showForm" :title="editId ? '编辑部署' : '新建策略部署'" width="xl" :mask-closable="false">
      <div class="scope-tabs">
          <button
            v-for="s in (['Trader', 'Exchange', 'Symbol'] as const)"
            :key="s"
            class="scope-tab"
            :class="{ active: scope === s }"
            @click="swapScope(s)"
          >
            <span class="scope-name">{{ scopeLabels[s] }}</span>
            <span class="scope-desc">{{ scopeDescriptions[s] }}</span>
          </button>
      </div>

      <div class="form-grid">
          <div class="form-group full">
            <label>策略模板</label>
            <a-select
              :model-value="formStrategyId"
              :disabled="!!editId"
              style="width: 100%"
              @change="(v) => formStrategyId = String(v)"
            >
              <a-option v-for="t in templates" :key="t.id" :value="t.id" :label="t.name" />
            </a-select>
          </div>

          <div v-if="scope !== 'Trader'" class="form-group full">
            <label>交易所账户</label>
            <a-select
              :model-value="formExchangeId"
              :disabled="!!editId"
              style="width: 100%"
              @change="(v) => formExchangeId = String(v)"
            >
              <a-option v-for="a in exchanges" :key="a.id" :value="a.id" :label="`${a.label} (${a.exchangeType})`" />
            </a-select>
          </div>

          <div v-if="scope === 'Symbol'" class="form-group full">
            <label>
              交易对
              <span class="selected-count">已选 {{ formSymbolIds.length }} 个</span>
            </label>
            <input v-model="symbolSearch" placeholder="搜索交易对..." class="symbol-search" />
            <div class="filter-bar">
              <div class="filter-item">
                <span class="filter-item-label">价格</span>
                <input v-model.number="priceMin" type="number" placeholder="≥" class="filter-input" />
                <span class="filter-sep">~</span>
                <input v-model.number="priceMax" type="number" placeholder="≤" class="filter-input" />
              </div>
              <div class="filter-item">
                <span class="filter-item-label">方向</span>
                <a-select
                  :model-value="changeDirection"
                  style="width: 100px"
                  @change="(v) => changeDirection = String(v) as 'all' | 'up' | 'down'"
                >
                  <a-option value="all" label="全部" />
                  <a-option value="up" label="上涨" />
                  <a-option value="down" label="下跌" />
                </a-select>
              </div>
              <div class="filter-item">
                <span class="filter-item-label">成交量</span>
                <input v-model.number="volMin" type="number" placeholder="≥" class="filter-input" />
              </div>
            </div>
            <div v-if="symbolsLoading" class="symbols-hint">加载中...</div>
            <div v-else-if="symbols.length === 0" class="symbols-hint">
              {{ formExchangeId ? '暂无 USDT 交易对数据' : '请先选择交易所' }}
            </div>
            <div v-else class="symbol-table-wrap">
              <table class="symbol-table">
                <thead>
                  <tr>
                    <th class="col-cb"></th>
                    <th class="col-sym sortable" @click="toggleSort('symbol')">交易对{{ sortArrow('symbol') }}</th>
                    <th class="col-price sortable" @click="toggleSort('price')">价格{{ sortArrow('price') }}</th>
                    <th class="col-chg sortable" @click="toggleSort('priceChangePercent')">24h 涨跌{{ sortArrow('priceChangePercent') }}</th>
                    <th class="col-vol sortable" @click="toggleSort('volume')">24h 量{{ sortArrow('volume') }}</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="s in sortedSymbols"
                    :key="s.symbol"
                    class="symbol-row"
                    :class="{ checked: formSymbolIds.includes(s.symbol) }"
                    @click="toggleSymbol(s.symbol)"
                  >
                    <td class="col-cb" @click.stop>
                      <input
                        type="checkbox"
                        :checked="formSymbolIds.includes(s.symbol)"
                        @change="toggleSymbol(s.symbol)"
                      />
                    </td>
                    <td class="col-sym">
                      <span class="sym-base">{{ renderSymbol(s.symbol).base }}</span>
                      <span class="sym-quote">{{ renderSymbol(s.symbol).quote }}</span>
                    </td>
                    <td class="col-price">{{ formatPrice(s.price) }}</td>
                    <td class="col-chg" :class="s.priceChangePercent >= 0 ? 'up' : 'down'">
                      {{ s.priceChangePercent >= 0 ? '+' : '' }}{{ s.priceChangePercent.toFixed(2) }}%
                    </td>
                    <td class="col-vol">{{ formatVolume(s.volume) }}</td>
                  </tr>
                </tbody>
              </table>
              <div v-if="sortedSymbols.length === 0" class="symbols-hint">无匹配交易对</div>
            </div>
          </div>

          <div class="form-group">
            <label>时间周期</label>
            <a-select
              :model-value="formTimeframe"
              style="width: 100%"
              @change="(v) => formTimeframe = String(v)"
            >
              <a-option v-for="tf in timeframes" :key="tf" :value="tf" :label="tf" />
            </a-select>
          </div>
      </div>

      <template #footer>
        <a-button type="primary" @click="save">
          <template #icon><icon-save /></template>
          保存
        </a-button>
      </template>
    </a-modal>

    <div v-if="errorMsg" class="error-banner">{{ errorMsg }}</div>

    <a-table
      :columns="columns"
      :data="displayDeployments"
      :loading="loading"
      table-layout-fixed
      :pagination="{
        current: page,
        pageSize: pageSize,
        total: deployments.length,
        simple: true,
        showTotal: true,
        showPageSize: true,
        pageSizeOptions: [10, 15, 20, 50, 100]
      }"
      page-position="top"
      @page-change="(p: number) => { page = p }"
      @page-size-change="(s: number) => { pageSize = s; page = 1 }"
      stripe
    >
      <template #scope="{ record }">
        <a-tag :color="scopeTagColors[record.scope] || ''">{{ scopeLabels[record.scope] || record.scope }}</a-tag>
      </template>
      <template #status="{ record }">
        <a-tag :color="tagStatusColors[normalizeStatus(record.status)] || ''">{{ getStatusLabel(record.status) }}</a-tag>
      </template>
      <template #updatedAt="{ record }">
        {{ record.updatedAt }}
      </template>
      <template #actions="{ record }">
        <div class="actions-group">
          <a-button
            size="mini"
            :status="isActive(record.status) ? 'warning' : 'success'"
            :disabled="toggleLoading === record.id || normalizeStatus(record.status) === 'Draft'"
            @click="toggle(record)"
          >
            <template #icon><icon-poweroff /></template>
            {{ toggleLoading === record.id ? '...' : isActive(record.status) ? '禁用' : '启用' }}
          </a-button>
          <a-button size="mini" :disabled="isActive(record.status)" @click="openEdit(record)">
            <template #icon><icon-edit /></template>
            编辑
          </a-button>
          <a-button size="mini" @click="router.push(`/traders/${traderId}/strategies/${record.id}/backtest`)">
            <template #icon><icon-common /></template>
            回测
          </a-button>
          <a-button size="mini" status="danger" :disabled="isActive(record.status)" @click="remove(record.id)">
            <template #icon><icon-delete /></template>
            删除
          </a-button>
        </div>
      </template>
    </a-table>
  </div>
</template>

<style scoped>
.strategy-page { padding: 0; }
.header-card { margin-bottom: 1rem; }
.header-card :deep(.arco-card-body) {
  padding: 0.75rem 1rem;
}
.header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.header-left {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}
.header-title {
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
}
.actions-group {
  display: flex;
  gap: 0.375rem;
  white-space: nowrap;
}
.error-banner { padding: 0.5rem 0.75rem; margin-bottom: 0.75rem; background: rgba(191, 72, 72, 0.06); border: 1px solid rgba(191, 72, 72, 0.18); border-radius: 6px; color: var(--accent-red); font-size: 0.85rem; }
.scope-tabs { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
.scope-tab {
  flex: 1; display: flex; flex-direction: column; align-items: center; gap: 0.25rem;
  padding: 0.625rem 0.75rem; border: 1px solid var(--glass-border); border-radius: 6px;
  background: rgba(255,255,255,0.35); cursor: pointer; transition: all 0.15s; text-align: center;
}
.scope-tab:hover { border-color: rgba(0, 0, 0, 0.14); }
.scope-tab.active { border-color: var(--accent-blue); background: rgba(79, 126, 201, 0.06); }
.scope-name { color: var(--text-primary); font-size: 0.85rem; font-weight: 600; }
.scope-desc { color: var(--text-muted); font-size: 0.7rem; line-height: 1.3; }
.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem; }
.form-group.full { grid-column: 1 / -1; }
.form-group { display: flex; flex-direction: column; gap: 0.25rem; }
.form-group label { color: var(--text-muted); font-size: 0.85rem; }
.form-group select, .form-group input { width: 100%; padding: 0.625rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-primary); box-sizing: border-box; }
.selected-count { color: var(--accent-blue); font-size: 0.75rem; margin-left: 0.5rem; }
.symbol-search { margin-bottom: 0.5rem; }
.filter-bar {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
  align-items: center;
}
.filter-item {
  display: flex;
  align-items: center;
  gap: 0.25rem;
}
.filter-item-label {
  color: var(--text-muted);
  font-size: 0.7rem;
  white-space: nowrap;
  flex-shrink: 0;
}
.filter-input {
  width: 70px;
  padding: 0.3rem 0.4rem;
  background: rgba(255, 255, 255, 0.45);
  color: var(--text-primary);
  border: 1px solid var(--glass-border);
  border-radius: 4px;
  font-size: 0.75rem;
}
.filter-sep { color: #475569; font-size: 0.75rem; }
.symbol-table-wrap {
  border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255, 255, 255, 0.35);
  max-height: 200px; overflow-y: auto;
}
.symbol-table th {
  background: rgba(0, 0, 0, 0.02); color: var(--text-muted); font-weight: 600;
  padding: 0.375rem 0.5rem; text-align: left; white-space: nowrap;
  border-bottom: 1px solid var(--glass-border);
}
.symbol-table { width: 100%; border-collapse: collapse; font-size: 0.75rem; }
.symbol-table thead { position: sticky; top: 0; z-index: 1; }
.symbol-table th {
  background: rgba(255,255,255,0.55); color: var(--text-muted); font-weight: 600;
  padding: 0.375rem 0.5rem; text-align: left; white-space: nowrap;
  border-bottom: 1px solid var(--glass-border);
}
.symbol-table th.sortable { cursor: pointer; user-select: none; }
.symbol-table th.sortable:hover { color: var(--text-muted); }
.symbol-table td { padding: 0.375rem 0.5rem; border-bottom: 1px solid var(--glass-border); white-space: nowrap; }
.symbol-row { cursor: pointer; transition: background 0.1s; }
.symbol-row:hover { background: rgba(79, 126, 201, 0.06); }
.symbol-row.checked { background: rgba(79, 126, 201, 0.1); }
.symbol-row.checked:hover { background: rgba(79, 126, 201, 0.14); }
.col-cb { width: 28px; padding-right: 0 !important; }
.col-sym { color: var(--text-primary); font-weight: 500; }
.sym-base { color: var(--text-primary); }
.sym-quote { color: var(--text-muted); font-size: 0.7rem; }
.col-price { color: var(--text-muted); text-align: right !important; }
.col-chg { text-align: right !important; font-weight: 600; }
.col-chg.up { color: var(--accent-green); }
.col-chg.down { color: var(--accent-red); }
.col-vol { color: var(--text-muted); text-align: right !important; }
.symbols-hint { color: var(--text-muted); font-size: 0.8rem; padding: 0.5rem; text-align: center; }
</style>
