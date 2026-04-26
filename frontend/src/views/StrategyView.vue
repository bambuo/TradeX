<script setup lang="ts">
import { ref, onMounted, watch, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { strategiesApi, type Strategy, type StrategyDeployment } from '../api/strategies'
import { exchangesApi, type ExchangeAccount } from '../api/exchanges'
import { formatSmallNumber } from '../utils/format'
import AppSelect from '../components/AppSelect.vue'

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
  Draft: 'var(--text-muted)', Backtesting: 'var(--accent-amber)', Passed: 'var(--accent-green)', Active: 'var(--accent-blue)', Disabled: 'var(--accent-red)'
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
  Trader: 'var(--text-muted)', Exchange: 'var(--accent-green)', Symbol: 'var(--accent-blue)'
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
            <AppSelect
              :options="templates.map(t => ({ label: t.name, value: t.id }))"
              :model-value="formStrategyId"
              :disabled="!!editId"
              full
              form
              @update:model-value="(v: string | number) => formStrategyId = String(v)"
            />
          </div>

          <div v-if="scope !== 'Trader'" class="form-group full">
            <label>交易所账户</label>
            <AppSelect
              :options="accounts.map(a => ({ label: `${a.label} (${a.exchangeType})`, value: a.id }))"
              :model-value="formExchangeId"
              :disabled="!!editId"
              full
              form
              @update:model-value="(v: string | number) => formExchangeId = String(v)"
            />
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
                <AppSelect
                  :options="[{ label: '全部', value: 'all' }, { label: '上涨', value: 'up' }, { label: '下跌', value: 'down' }]"
                  :model-value="changeDirection"
                  @update:model-value="(v: string | number) => changeDirection = v as 'all' | 'up' | 'down'"
                />
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
            <AppSelect
              :options="timeframes.map(tf => ({ label: tf, value: tf }))"
              :model-value="formTimeframe"
              full
              form
              @update:model-value="(v: string | number) => formTimeframe = String(v)"
            />
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
            <span class="status-indicator">
              <span class="status-dot" :style="{ background: scopeBadgeColors[d.scope] || 'var(--text-muted)' }" />
              <span :style="{ color: scopeBadgeColors[d.scope] || 'var(--text-muted)' }">{{ scopeLabels[d.scope] || d.scope }}</span>
            </span>
          </td>
          <td>{{ getTemplateName(d.strategyId) }}</td>
          <td>{{ d.exchangeId && d.exchangeId !== '00000000-0000-0000-0000-000000000000' ? getExchangeLabel(d.exchangeId) : '-' }}</td>
          <td class="symbol-cell">{{ d.scope === 'Symbol' ? parseSymbolIds(d.symbolIds).join(', ') : '-' }}</td>
          <td>{{ d.timeframe }}</td>
          <td>
            <span class="status-indicator">
              <span class="status-dot" :style="{ background: getStatusColor(d.status) }" />
              <span :style="{ color: getStatusColor(d.status) }">{{ getStatusLabel(d.status) }}</span>
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
.header-left h2 { margin: 0; color: var(--text-primary); }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.625rem 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); font-size: 0.85rem; }
.table th { color: var(--text-muted); font-weight: 600; }
.symbol-cell { max-width: 200px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.actions { display: flex; gap: 0.375rem; flex-wrap: wrap; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.error-banner { padding: 0.5rem 0.75rem; margin-bottom: 0.75rem; background: rgba(191, 72, 72, 0.06); border: 1px solid rgba(191, 72, 72, 0.18); border-radius: 6px; color: var(--accent-red); font-size: 0.85rem; }
.status-indicator { display: inline-flex; align-items: center; gap: 0.3rem; }
.status-dot { width: 0.5rem; height: 0.5rem; border-radius: 50%; flex-shrink: 0; }
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
