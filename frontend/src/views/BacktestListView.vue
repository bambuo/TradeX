<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { tradersApi, type Trader } from '../api/traders'
import { strategiesApi, type Strategy } from '../api/strategies'
import { exchangesApi, type Exchange } from '../api/exchanges'
import { backtestsApi, type BacktestTask } from '../api/backtests'
import { formatSmallNumber } from '../utils/format'

const router = useRouter()

interface TaskItem {
  traderId: string
  traderName: string
  bindingId: string
  bindingName: string
  task: BacktestTask
}

interface PairInfo {
  symbol: string
  price: number
  priceChangePercent: number
  volume: number
  highPrice: number
  lowPrice: number
}

const tasks = ref<TaskItem[]>([])
const loading = ref(true)
const error = ref('')

const statusLabels: Record<string, string> = {
  Pending: '待处理',
  Running: '运行中',
  Completed: '已完成',
  Failed: '失败'
}

const statusColors: Record<string, string> = {
  Pending: '', Running: 'orange', Completed: 'green', Failed: 'red'
}

const showForm = ref(false)
const traders = ref<Trader[]>([])
const strategies = ref<Strategy[]>([])
const exchanges = ref<Exchange[]>([])
const pairs = ref<PairInfo[]>([])
const pairsLoading = ref(false)
const pairSearch = ref('')
const sortKey = ref<'symbol' | 'price' | 'priceChangePercent' | 'volume'>('volume')
const sortDesc = ref(true)
const priceMin = ref<number | null>(null)
const priceMax = ref<number | null>(null)
const changeDirection = ref<'all' | 'up' | 'down'>('all')
const volMin = ref<number | null>(null)
const formTraderId = ref('')
const formStrategyId = ref('')
const formExchangeId = ref('')
const formPicks = ref<string[]>([])
const formTimeframe = ref('1h')
const formStartAt = ref('')
const formEndAt = ref('')
const formCapital = ref(1000)
const formSaving = ref(false)
const formError = ref('')

const timeframes = ['1m', '5m', '15m', '30m', '1h', '4h', '1d']

const quickDateOptions = [
  { key: '1d', label: '1 天', days: 1 },
  { key: '3d', label: '3 天', days: 3 },
  { key: '1w', label: '一周', days: 7 },
  { key: '1m', label: '一个月', days: 30 }
]
const activeQuickDate = ref('1w')

function getErrorMessage(e: unknown, fallback: string): string {
  const response = (e as { response?: { data?: { message?: string; error?: string } } }).response
  return response?.data?.message || response?.data?.error || fallback
}

function toTaskItem(task: BacktestTask): TaskItem {
  const strategy = strategies.value.find(s => s.id === task.strategyId)

  return {
    traderId: '',
    traderName: '全局',
    bindingId: '',
    bindingName: task.strategyName || strategy?.name || task.strategyId,
    task
  }
}

async function loadTasks(showLoading = true) {
  if (showLoading) loading.value = true
  error.value = ''
  try {
    const { data } = await backtestsApi.getTasks()
    tasks.value = data
      .map(toTaskItem)
      .sort((a, b) => b.task.createdAt.localeCompare(a.task.createdAt))
  } catch (e) {
    tasks.value = []
    error.value = getErrorMessage(e, '加载回测任务失败')
    throw e
  } finally {
    if (showLoading) loading.value = false
  }
}

const sortedPairs = computed(() => {
  let list = pairs.value
  if (pairSearch.value) {
    const q = pairSearch.value.toLowerCase()
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

function renderPair(sym: string): { base: string; quote: string } {
  if (sym.endsWith('USDT')) return { base: sym.slice(0, -4), quote: 'USDT' }
  const m = sym.match(/^([A-Za-z]+)(BTC|ETH|BNB|USDC|DAI)$/)
  if (m) return { base: m[1], quote: m[2] }
  return { base: sym, quote: '' }
}

onMounted(async () => {
  try {
    const [tradersRes, strategiesRes, exchangesRes] = await Promise.all([
      tradersApi.getAll(),
      strategiesApi.getAllPure(),
      exchangesApi.getAll()
    ])
    traders.value = tradersRes.data
    strategies.value = strategiesRes.data.data ?? []
    exchanges.value = exchangesRes.data.data ?? []
    await loadTasks(false)
  } catch (e) {
    if (!error.value) error.value = getErrorMessage(e, '加载回测任务失败')
  } finally {
    loading.value = false
  }
})

function openDetail(item: TaskItem) {
  router.push(`/backtests/tasks/${item.task.id}`)
}

function formatDate(dt: string): string {
  if (!dt) return '-'
  return new Date(dt).toLocaleString('zh-CN', {
    year: 'numeric', month: '2-digit', day: '2-digit',
    hour: '2-digit', minute: '2-digit'
  })
}

async function fetchExchangePairs(exchangeId: string) {
  if (!exchangeId) return
  pairsLoading.value = true
  pairs.value = []
  pairSearch.value = ''
  sortKey.value = 'volume'
  sortDesc.value = true
  priceMin.value = null
  priceMax.value = null
  changeDirection.value = 'all'
  volMin.value = null
  try {
    const { data } = await exchangesApi.getPairs(exchangeId)
    pairs.value = (data.data ?? []).map((s: { pair: string; price?: number; priceChangePercent?: number; volume?: number; highPrice?: number; lowPrice?: number }) => ({
      symbol: s.pair,
      price: s.price ?? 0,
      priceChangePercent: s.priceChangePercent ?? 0,
      volume: s.volume ?? 0,
      highPrice: s.highPrice ?? 0,
      lowPrice: s.lowPrice ?? 0
    }))
    formPicks.value = []
  } catch (e) {
    pairs.value = []
    formError.value = getErrorMessage(e, '加载交易对失败')
  } finally {
    pairsLoading.value = false
  }
}

function openCreate() {
  formTraderId.value = traders.value[0]?.id || ''
  formStrategyId.value = strategies.value[0]?.id || ''
  formExchangeId.value = exchanges.value[0]?.id || ''
  formPicks.value = []
  formTimeframe.value = '1h'
  formCapital.value = 1000
  formError.value = ''
  applyQuickDate('1w', 7)
  if (formExchangeId.value) fetchExchangePairs(formExchangeId.value)
  showForm.value = true
}

function applyQuickDate(key: string, days: number) {
  activeQuickDate.value = key
  const now = new Date()
  formEndAt.value = now.toISOString().slice(0, 16)
  const start = new Date(now.getTime() - days * 24 * 60 * 60 * 1000)
  formStartAt.value = start.toISOString().slice(0, 16)
}

function onExchangeChange(id: string) {
  formExchangeId.value = id
  formPicks.value = []
  fetchExchangePairs(id)
}

function togglePair(Pair: string) {
  formPicks.value.includes(Pair) ? formPicks.value = formPicks.value.filter(s => s !== Pair) : formPicks.value.push(Pair)
}

async function save() {
  formError.value = ''
  if (!formStrategyId.value) { formError.value = '请选择策略'; return }
  if (!formExchangeId.value) { formError.value = '请选择交易所'; return }
  if (formPicks.value.length === 0) { formError.value = '请选择交易对'; return }
  if (!formStartAt.value || !formEndAt.value) { formError.value = '请选择回测时间范围'; return }
  if (!formCapital.value || formCapital.value <= 0) { formError.value = '请输入有效本金'; return }

  formSaving.value = true
  try {
    const startDate = new Date(formStartAt.value)
    const endDate = new Date(formEndAt.value)
    for (const pair of formPicks.value) {
      await backtestsApi.start(
        formStrategyId.value,
        formExchangeId.value,
        pair,
        formTimeframe.value,
        startDate.toISOString(),
        endDate.toISOString(),
        formCapital.value
      )
    }
    showForm.value = false
    await loadTasks(false)
  } catch (e) {
    const message = getErrorMessage(e, '创建回测失败')
    if (showForm.value) formError.value = message
    else error.value = message
  } finally {
    formSaving.value = false
  }
}
</script>

<template>
  <div class="backtest-list-page">
    <a-card class="header-card">
      <div class="header-row">
        <div class="header-left">
          <span class="header-title">回测任务</span>
        </div>
        <a-button type="primary" @click="openCreate">
          <template #icon><icon-plus /></template>
          新建回测
        </a-button>
      </div>
    </a-card>

    <a-modal v-model:visible="showForm" title="新建回测" width="960px" :mask-closable="false">
      <div class="modal-body">
        <div class="settings-grid">
          <div class="form-group">
            <label>策略</label>
            <a-select
              :model-value="formStrategyId"
              style="width: 100%"
              @change="(v: unknown) => formStrategyId = String(v)"
            >
              <a-option v-for="s in strategies" :key="s.id" :value="s.id" :label="s.name" />
            </a-select>
          </div>

          <div class="form-group">
            <label>交易所</label>
            <a-select
              :model-value="formExchangeId"
              style="width: 100%"
              @change="(v: unknown) => onExchangeChange(String(v))"
            >
              <a-option v-for="e in exchanges" :key="e.id" :value="e.id" :label="`${e.label} (${e.exchangeType})`" />
            </a-select>
          </div>

          <div class="form-group">
            <label>时间周期</label>
            <a-select
              :model-value="formTimeframe"
              style="width: 100%"
              @change="(v: unknown) => formTimeframe = String(v)"
            >
              <a-option v-for="tf in timeframes" :key="tf" :value="tf" :label="tf" />
            </a-select>
          </div>

          <div class="form-group">
            <label>本金 (USDT)</label>
            <a-input-number
              :model-value="formCapital"
              :min="100"
              :step="100"
              style="width: 100%"
              @change="(v: unknown) => formCapital = Number(v as number)"
            />
          </div>
        </div>

        <div class="form-group full">
          <label>回测时间范围</label>
          <div class="quick-dates">
            <button
              v-for="opt in quickDateOptions"
              :key="opt.key"
              class="quick-date-btn"
              :class="{ active: activeQuickDate === opt.key }"
              @click="applyQuickDate(opt.key, opt.days)"
            >{{ opt.label }}</button>
          </div>
        </div>

        <div class="form-group full">
          <label>
            交易对
            <span class="selected-count">已选 {{ formPicks.length ? formPicks.length + ' 个' : '无' }}</span>
          </label>
          <input v-model="pairSearch" placeholder="搜索交易对..." class="pair-search" />
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
                @change="(v: unknown) => changeDirection = String(v) as 'all' | 'up' | 'down'"
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
          <div class="pair-table-wrap">
            <table class="pair-table">
              <thead>
                <tr>
                  <th class="col-cb"></th>
                  <th class="col-sym sortable" @click="toggleSort('symbol')">交易对{{ sortArrow('symbol') }}</th>
                  <th class="col-price sortable" @click="toggleSort('price')">价格{{ sortArrow('price') }}</th>
                  <th class="col-chg sortable" @click="toggleSort('priceChangePercent')">24h 涨跌{{ sortArrow('priceChangePercent') }}</th>
                  <th class="col-vol sortable" @click="toggleSort('volume')">24h 量{{ sortArrow('volume') }}</th>
                </tr>
              </thead>
              <tbody v-if="pairsLoading">
                <tr><td colspan="5" class="table-status">加载中...</td></tr>
              </tbody>
              <tbody v-else-if="sortedPairs.length === 0">
                <tr><td colspan="5" class="table-status">{{ formExchangeId ? '暂无交易对数据' : '请先选择交易所' }}</td></tr>
              </tbody>
              <tbody v-else>
                <tr
                  v-for="s in sortedPairs"
                  :key="s.symbol"
                  class="pair-row"
                  :class="{ checked: formPicks.includes(s.symbol) }"
                  @click="togglePair(s.symbol)"
                >
                  <td class="col-cb" @click.stop>
                    <input
                      type="checkbox"
                      :checked="formPicks.includes(s.symbol)"
                      @change="togglePair(s.symbol)"
                    />
                  </td>
                  <td class="col-sym">
                    <span class="sym-base">{{ renderPair(s.symbol).base }}</span>
                    <span class="sym-quote">{{ renderPair(s.symbol).quote }}</span>
                  </td>
                  <td class="col-price">{{ formatPrice(s.price) }}</td>
                  <td class="col-chg" :class="s.priceChangePercent >= 0 ? 'up' : 'down'">
                    {{ s.priceChangePercent >= 0 ? '+' : '' }}{{ s.priceChangePercent.toFixed(2) }}%
                  </td>
                  <td class="col-vol">{{ formatVolume(s.volume) }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div v-if="formError" class="form-error">{{ formError }}</div>

      <template #footer>
        <a-button type="primary" :loading="formSaving" @click="save">
          <template #icon><icon-play-arrow-fill /></template>
          开始回测
        </a-button>
      </template>
    </a-modal>

    <div v-if="loading" class="loading-state">加载中...</div>
    <div v-else-if="error" class="empty-state">{{ error }}</div>
    <div v-else-if="tasks.length === 0" class="empty-state">
      <strong>暂无回测任务</strong>
      <span>点击上方按钮新建回测</span>
    </div>
    <a-table
      v-else
      :columns="[
        { title: '#', width: 40, render: ({ rowIndex }: { rowIndex: number }) => rowIndex + 1 },
        { title: '交易员', dataIndex: 'traderName', width: 120 },
        { title: '策略', dataIndex: 'strategyName', width: 150, ellipsis: true },
        { title: '交易对', dataIndex: 'pair', width: 110 },
        { title: '周期', dataIndex: 'timeframe', width: 60 },
        { title: '本金', dataIndex: 'capital', width: 80 },
        { title: '开始', dataIndex: 'startAt', width: 150 },
        { title: '结束', dataIndex: 'endAt', width: 150 },
        { title: '状态', slotName: 'status', width: 80 },
        { title: '操作', slotName: 'actions', width: 80 }
      ]"
      :data="tasks.map(t => ({
        ...t,
        key: t.task.id,
        strategyName: t.task.strategyName || t.bindingName,
        pair: t.task.pair,
        timeframe: t.task.timeframe,
        capital: t.task.initialCapital?.toFixed(0) || '-',
        startAt: formatDate(t.task.startAt),
        endAt: formatDate(t.task.endAt)
      }))"
      :pagination="{
        pageSize: 15,
        showTotal: true,
        simple: true
      }"
      stripe
    >
      <template #status="{ record }">
        <a-tag :color="statusColors[record.task.status] || ''">
          {{ statusLabels[record.task.status] || record.task.status }}
        </a-tag>
      </template>
      <template #actions="{ record }">
        <a-button size="mini" @click="openDetail(record)">
          详情
        </a-button>
      </template>
    </a-table>
  </div>
</template>

<style scoped>
.backtest-list-page { padding: 0; }
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
.loading-state, .empty-state {
  text-align: center; color: var(--text-muted); padding: 3rem 1rem;
  display: flex; flex-direction: column; gap: 0.35rem;
}
.form-error { color: var(--accent-red); font-size: 0.85rem; margin-bottom: 0.5rem; }
.modal-body { display: flex; flex-direction: column; gap: 1rem; }
.settings-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem; }
.form-group { display: flex; flex-direction: column; gap: 0.25rem; }
.form-group.full { display: flex; flex-direction: column; gap: 0.25rem; }
.form-group label { color: var(--text-muted); font-size: 0.85rem; }
.form-input {
  width: 100%; padding: 0.625rem;
  border: 1px solid var(--glass-border); border-radius: 4px;
  background: rgba(255,255,255,0.35); color: var(--text-primary);
  box-sizing: border-box;
}
.quick-dates { display: flex; gap: 0.5rem; margin-bottom: 0.5rem; }
.quick-date-btn {
  flex: 1; padding: 0.4rem 0.5rem; border: 1px solid var(--glass-border);
  border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-muted);
  cursor: pointer; font-size: 0.8rem; text-align: center; transition: all 0.12s;
}
.quick-date-btn:hover { border-color: rgba(0,0,0,0.14); }
.quick-date-btn.active { border-color: var(--accent-blue); background: rgba(79,126,201,0.08); color: var(--accent-blue); font-weight: 600; }
.date-range { display: flex; align-items: center; gap: 0.5rem; }
.date-field { flex: 1; display: flex; flex-direction: column; gap: 0.2rem; }
.date-label { font-size: 0.75rem; color: var(--text-muted); }
.date-sep { color: var(--text-muted); font-size: 0.9rem; padding-top: 1.2rem; }
.pairs-hint { color: var(--text-muted); font-size: 0.8rem; padding: 0.5rem 0; text-align: center; }
.selected-count { color: var(--accent-blue); font-size: 0.75rem; margin-left: 0.5rem; }
.pair-search { margin-bottom: 0.5rem; width: 100%; padding: 0.375rem 0.5rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-primary); box-sizing: border-box; }
.filter-bar {
  display: flex;
  gap: 1rem;
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
.pair-table-wrap {
  border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255, 255, 255, 0.35);
  max-height: 240px; overflow-y: auto;
}
.pair-table { width: 100%; border-collapse: collapse; font-size: 0.75rem; }
.pair-table thead { position: sticky; top: 0; z-index: 1; }
.pair-table th {
  background: #fff; color: var(--text-muted); font-weight: 600;
  padding: 0.375rem 0.5rem; text-align: left; white-space: nowrap;
  border-bottom: 1px solid var(--glass-border);
}
.pair-table th.sortable { cursor: pointer; user-select: none; }
.pair-table th.sortable:hover { color: var(--text-muted); }
.pair-table td { padding: 0.375rem 0.5rem; border-bottom: 1px solid var(--glass-border); white-space: nowrap; }
.pair-row { cursor: pointer; transition: background 0.1s; }
.pair-row:hover { background: rgba(79, 126, 201, 0.06); }
.pair-row.checked { background: rgba(79, 126, 201, 0.1); }
.pair-row.checked:hover { background: rgba(79, 126, 201, 0.14); }
.table-status { text-align: center; padding: 1.5rem 0; color: var(--text-muted); font-size: 0.8rem; }
.col-cb { width: 28px; text-align: center; }
.col-cb input { display: inline-block; vertical-align: middle; }
.col-sym { color: var(--text-primary); font-weight: 500; }
.sym-base { color: var(--text-primary); }
.sym-quote { color: var(--text-muted); font-size: 0.7rem; }
.col-price { color: var(--text-muted); text-align: right !important; }
.col-chg { text-align: right !important; font-weight: 600; }
.col-chg.up { color: var(--accent-green); }
.col-chg.down { color: var(--accent-red); }
.col-vol { color: var(--text-muted); text-align: right !important; }
</style>
