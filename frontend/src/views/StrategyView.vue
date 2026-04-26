<script setup lang="ts">
import { ref, onMounted, watch, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { strategiesApi, type Strategy, type StrategyDeployment } from '../api/strategies'
import { exchangesApi, type ExchangeAccount } from '../api/exchanges'
import { formatSmallNumber } from '../utils/format'

const route = useRoute()
const router = useRouter()
const traderId = route.params.traderId as string

const deployments = ref<StrategyDeployment[]>([])
const templates = ref<Strategy[]>([])
const accounts = ref<ExchangeAccount[]>([])
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const toggleLoading = ref<string | null>(null)

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

const statusColors: Record<string, string> = {
  Draft: '#94a3b8', Backtesting: '#f59e0b', Passed: '#22c55e', Active: '#38bdf8', Disabled: '#ef4444'
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

function getStatusColor(status: unknown): string {
  const normalized = normalizeStatus(status)
  return statusColors[normalized] ?? '#94a3b8'
}

function isActive(status: unknown): boolean {
  return normalizeStatus(status) === 'Active'
}

function formatUtcTime(value: string): string {
  if (!value) return '-'
  const normalized = /[zZ]$|[+-]\d{2}:\d{2}$/.test(value) ? value : `${value}Z`
  const date = new Date(normalized)
  if (Number.isNaN(date.getTime())) return '-'
  return date.toLocaleString('zh-CN', { hour12: false })
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
    accounts.value = accRes.data.data ?? []
  } finally {
    loading.value = false
  }
}

function getExchangeLabel(exchangeId: string): string {
  return accounts.value.find(a => a.id === exchangeId)?.label ?? exchangeId
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

const scopeBadgeColors: Record<string, string> = {
  Trader: '#94a3b8', Exchange: '#22c55e', Symbol: '#38bdf8'
}

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
  formExchangeId.value = accounts.value[0]?.id ?? ''
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
    formExchangeId.value = accounts.value[0]?.id ?? ''
    formSymbolIds.value = []
  } else {
    formExchangeId.value = (formExchangeId.value || accounts.value[0]?.id) ?? ''
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
    <header class="page-header">
      <div class="header-left">
        <AppButton variant="ghost" size="sm" icon="back" @click="router.push('/traders')">返回</AppButton>
        <h2>策略部署</h2>
      </div>
      <AppButton variant="primary" icon="plus" @click="openCreate">新建部署</AppButton>
    </header>

    <AppModal v-model="showForm" :title="editId ? '编辑部署' : '新建策略部署'" width="xl">
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
            <select v-model="formStrategyId" :disabled="!!editId">
              <option v-for="t in templates" :key="t.id" :value="t.id">{{ t.name }}</option>
            </select>
          </div>

          <div v-if="scope !== 'Trader'" class="form-group full">
            <label>交易所账户</label>
            <select v-model="formExchangeId" :disabled="!!editId">
              <option v-for="a in accounts" :key="a.id" :value="a.id">
                {{ a.label }} ({{ a.exchangeType }})
              </option>
            </select>
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
                <select v-model="changeDirection" class="filter-select">
                  <option value="all">全部</option>
                  <option value="up">上涨</option>
                  <option value="down">下跌</option>
                </select>
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
            <select v-model="formTimeframe">
              <option v-for="tf in timeframes" :key="tf" :value="tf">{{ tf }}</option>
            </select>
          </div>
      </div>

      <template #footer>
        <AppButton icon="close" @click="showForm = false">取消</AppButton>
        <AppButton variant="primary" icon="save" @click="save">保存</AppButton>
      </template>
    </AppModal>

    <div v-if="loading">加载中...</div>
    <div v-if="errorMsg && !loading" class="error-banner">{{ errorMsg }}</div>
    <table v-if="!loading" class="table">
        <thead>
          <tr>
            <th>作用域</th>
            <th>策略</th>
            <th>交易所</th>
            <th>交易对</th>
            <th>周期</th>
            <th>状态</th>
            <th>更新时间</th>
            <th>操作</th>
          </tr>
        </thead>
      <tbody>
        <tr v-for="d in deployments" :key="d.id">
          <td>
            <span class="scope-badge" :style="{ background: scopeBadgeColors[d.scope] || '#94a3b8' }">
              {{ scopeLabels[d.scope] || d.scope }}
            </span>
          </td>
          <td>{{ getTemplateName(d.strategyId) }}</td>
          <td>{{ d.exchangeId && d.exchangeId !== '00000000-0000-0000-0000-000000000000' ? getExchangeLabel(d.exchangeId) : '-' }}</td>
          <td class="symbol-cell">{{ d.scope === 'Symbol' ? parseSymbolIds(d.symbolIds).join(', ') : '-' }}</td>
          <td>{{ d.timeframe }}</td>
          <td>
            <span class="status-badge" :style="{ background: getStatusColor(d.status) }">
              {{ getStatusLabel(d.status) }}
            </span>
          </td>
          <td>{{ formatUtcTime(d.updatedAt) }}</td>
          <td class="actions">
            <AppButton
              size="sm"
              :variant="isActive(d.status) ? 'warning' : 'success'"
              icon="power"
              :disabled="toggleLoading === d.id || normalizeStatus(d.status) === 'Draft'"
              @click="toggle(d)"
            >{{ toggleLoading === d.id ? '...' : isActive(d.status) ? '禁用' : '启用' }}</AppButton>
            <AppButton size="sm" icon="edit" :disabled="isActive(d.status)" @click="openEdit(d)">编辑</AppButton>
            <AppButton size="sm" icon="chart" @click="router.push(`/traders/${traderId}/strategies/${d.id}/backtest`)">回测</AppButton>
            <AppButton size="sm" variant="danger" icon="trash" :disabled="isActive(d.status)" @click="remove(d.id)">删除</AppButton>
          </td>
        </tr>
        <tr v-if="deployments.length === 0">
          <td colspan="8" class="empty">暂无策略部署</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.strategy-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.header-left { display: flex; align-items: center; gap: 1rem; }
.header-left h2 { margin: 0; color: #e2e8f0; }
.btn-back { background: none; border: 1px solid #475569; color: #94a3b8; padding: 0.25rem 0.75rem; border-radius: 4px; cursor: pointer; font-size: 0.9rem; }
.btn-primary { padding: 0.5rem 1rem; background: #38bdf8; color: #0f172a; border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; }
.btn-small { padding: 0.25rem 0.75rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
.btn-small:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-danger { color: #ef4444; border-color: #ef4444; background: transparent; }
.btn-ok { color: #22c55e; border-color: #22c55e; background: transparent; }
.btn-warn { color: #f59e0b; border-color: #f59e0b; background: transparent; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.625rem 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; font-size: 0.85rem; }
.table th { color: #94a3b8; font-weight: 600; }
.symbol-cell { max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.actions { display: flex; gap: 0.375rem; flex-wrap: wrap; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.error-banner { padding: 0.5rem 0.75rem; margin-bottom: 0.75rem; background: rgba(239,68,68,0.1); border: 1px solid rgba(239,68,68,0.3); border-radius: 6px; color: #ef4444; font-size: 0.85rem; }
.status-badge, .scope-badge { display: inline-block; padding: 0.125rem 0.5rem; border-radius: 999px; color: #0f172a; font-size: 0.75rem; font-weight: 600; }
.scope-tabs { display: flex; gap: 0.5rem; margin-bottom: 1rem; }
.scope-tab {
  flex: 1; display: flex; flex-direction: column; align-items: center; gap: 0.25rem;
  padding: 0.625rem 0.75rem; border: 1px solid #334155; border-radius: 6px;
  background: #0f172a; cursor: pointer; transition: all 0.15s; text-align: center;
}
.scope-tab:hover { border-color: #475569; }
.scope-tab.active { border-color: #38bdf8; background: rgba(56, 189, 248, 0.06); }
.scope-name { color: #e2e8f0; font-size: 0.85rem; font-weight: 600; }
.scope-desc { color: #64748b; font-size: 0.7rem; line-height: 1.3; }
.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem; }
.form-group.full { grid-column: 1 / -1; }
.form-group { display: flex; flex-direction: column; gap: 0.25rem; }
.form-group label { color: #94a3b8; font-size: 0.85rem; }
.form-group select, .form-group input { width: 100%; padding: 0.625rem; border: 1px solid #334155; border-radius: 4px; background: #0f172a; color: #e2e8f0; box-sizing: border-box; }
.selected-count { color: #38bdf8; font-size: 0.75rem; margin-left: 0.5rem; }
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
  color: #64748b;
  font-size: 0.7rem;
  white-space: nowrap;
  flex-shrink: 0;
}
.filter-input {
  width: 70px;
  padding: 0.3rem 0.4rem;
  background: #0f172a;
  color: #e2e8f0;
  border: 1px solid #334155;
  border-radius: 4px;
  font-size: 0.75rem;
}
.filter-sep { color: #475569; font-size: 0.75rem; }
.filter-select {
  padding: 0.3rem 0.4rem;
  background: #0f172a;
  color: #e2e8f0;
  border: 1px solid #334155;
  border-radius: 4px;
  font-size: 0.75rem;
}
.symbol-table-wrap {
  border: 1px solid #334155; border-radius: 4px; background: #0f172a;
  max-height: 200px; overflow-y: auto;
}
.symbol-table { width: 100%; border-collapse: collapse; font-size: 0.75rem; }
.symbol-table thead { position: sticky; top: 0; z-index: 1; }
.symbol-table th {
  background: #1e293b; color: #64748b; font-weight: 600;
  padding: 0.375rem 0.5rem; text-align: left; white-space: nowrap;
  border-bottom: 1px solid #334155;
}
.symbol-table th.sortable { cursor: pointer; user-select: none; }
.symbol-table th.sortable:hover { color: #94a3b8; }
.symbol-table td { padding: 0.375rem 0.5rem; border-bottom: 1px solid rgba(51, 65, 85, 0.4); white-space: nowrap; }
.symbol-row { cursor: pointer; transition: background 0.1s; }
.symbol-row:hover { background: rgba(56, 189, 248, 0.06); }
.symbol-row.checked { background: rgba(56, 189, 248, 0.1); }
.symbol-row.checked:hover { background: rgba(56, 189, 248, 0.14); }
.col-cb { width: 28px; padding-right: 0 !important; }
.col-sym { color: #e2e8f0; font-weight: 500; }
.sym-base { color: #e2e8f0; }
.sym-quote { color: #64748b; font-size: 0.7rem; }
.col-price { color: #94a3b8; text-align: right !important; }
.col-chg { text-align: right !important; font-weight: 600; }
.col-chg.up { color: #22c55e; }
.col-chg.down { color: #ef4444; }
.col-vol { color: #64748b; text-align: right !important; }
.symbols-hint { color: #64748b; font-size: 0.8rem; padding: 0.5rem; text-align: center; }
</style>
