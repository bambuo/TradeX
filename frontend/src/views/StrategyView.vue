<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { strategiesApi, type Strategy, type CreateStrategyRequest, type UpdateStrategyRequest } from '../api/strategies'
import { exchangeAccountsApi, type ExchangeAccount } from '../api/exchangeAccounts'
import ConditionTreeEditor, { type ConditionNode } from '../components/ConditionTreeEditor.vue'

const route = useRoute()
const router = useRouter()
const traderId = route.params.traderId as string

const strategies = ref<Strategy[]>([])
const accounts = ref<ExchangeAccount[]>([])
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const toggleLoading = ref<string | null>(null)

const formName = ref('')
const formExchangeId = ref('')
const formSymbolIds = ref('')
const formTimeframe = ref('15m')
const formEntryCondition = ref('{}')
const formExitCondition = ref('{}')
const formExecutionRule = ref('{}')

const timeframes = ['1m', '5m', '15m', '30m', '1h', '4h', '1d']

const entryConditionNode = computed({
  get: () => { try { return JSON.parse(formEntryCondition.value) } catch { return { operator: 'AND', conditions: [] } } },
  set: (val: ConditionNode) => { formEntryCondition.value = JSON.stringify(val, null, 2) }
})
const exitConditionNode = computed({
  get: () => { try { return JSON.parse(formExitCondition.value) } catch { return { operator: 'AND', conditions: [] } } },
  set: (val: ConditionNode) => { formExitCondition.value = JSON.stringify(val, null, 2) }
})

const statusLabels: Record<string, string> = {
  Draft: '草稿',
  Backtesting: '回测中',
  Passed: '已通过',
  Active: '活跃',
  Disabled: '已禁用'
}

const statusColors: Record<string, string> = {
  Draft: '#94a3b8',
  Backtesting: '#f59e0b',
  Passed: '#22c55e',
  Active: '#38bdf8',
  Disabled: '#ef4444'
}

async function load() {
  loading.value = true
  try {
    const [stratRes, accRes] = await Promise.all([
      strategiesApi.getAll(traderId),
      exchangeAccountsApi.getAll(traderId)
    ])
    strategies.value = stratRes.data
    accounts.value = accRes.data
  } finally {
    loading.value = false
  }
}

function getExchangeLabel(exchangeId: string): string {
  return accounts.value.find(a => a.id === exchangeId)?.label ?? exchangeId
}

function openCreate() {
  editId.value = null
  formName.value = ''
  formExchangeId.value = accounts.value[0]?.id ?? ''
  formSymbolIds.value = ''
  formTimeframe.value = '15m'
  formEntryCondition.value = '{}'
  formExitCondition.value = '{}'
  formExecutionRule.value = '{}'
  showForm.value = true
}

function openEdit(strategy: Strategy) {
  editId.value = strategy.id
  formName.value = strategy.name
  formExchangeId.value = strategy.exchangeId
  formSymbolIds.value = strategy.symbolIds
  formTimeframe.value = strategy.timeframe
  formEntryCondition.value = strategy.entryConditionJson
  formExitCondition.value = strategy.exitConditionJson
  formExecutionRule.value = strategy.executionRuleJson
  showForm.value = true
}

async function save() {
  if (editId.value) {
    const payload: UpdateStrategyRequest = {}
    if (formName.value) payload.name = formName.value
    payload.symbolIds = formSymbolIds.value || '[]'
    payload.timeframe = formTimeframe.value
    payload.entryConditionJson = formEntryCondition.value || '{}'
    payload.exitConditionJson = formExitCondition.value || '{}'
    payload.executionRuleJson = formExecutionRule.value || '{}'
    await strategiesApi.update(traderId, editId.value, payload)
  } else {
    const payload: CreateStrategyRequest = {
      name: formName.value,
      exchangeId: formExchangeId.value,
      symbolIds: formSymbolIds.value || '[]',
      timeframe: formTimeframe.value,
      entryConditionJson: formEntryCondition.value || '{}',
      exitConditionJson: formExitCondition.value || '{}',
      executionRuleJson: formExecutionRule.value || '{}'
    }
    await strategiesApi.create(traderId, payload)
  }
  showForm.value = false
  await load()
}

async function remove(id: string) {
  await strategiesApi.delete(traderId, id)
  await load()
}

async function toggle(strategy: Strategy) {
  toggleLoading.value = strategy.id
  try {
    await strategiesApi.toggle(traderId, strategy.id, strategy.status !== 'Active')
    await load()
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
        <button class="btn-back" @click="router.push(`/traders/${traderId}/exchanges`)">← 交易所</button>
        <h2>交易策略</h2>
      </div>
      <button class="btn-primary" @click="openCreate">新建策略</button>
    </header>

    <div v-if="showForm" class="modal-overlay" @click.self="showForm = false">
      <div class="modal modal-wide">
        <h3>{{ editId ? '编辑策略' : '新建策略' }}</h3>
        <div class="form-grid">
          <div class="form-group">
            <label>策略名称</label>
            <input v-model="formName" placeholder="如：ETH 趋势追踪" />
          </div>
          <div class="form-group">
            <label>交易所账户</label>
            <select v-model="formExchangeId" :disabled="!!editId">
              <option v-for="a in accounts" :key="a.id" :value="a.id">
                {{ a.label }} ({{ a.exchangeType }})
              </option>
            </select>
          </div>
          <div class="form-group">
            <label>交易对 (JSON 数组)</label>
            <input v-model="formSymbolIds" placeholder='["BTCUSDT", "ETHUSDT"]' />
          </div>
          <div class="form-group">
            <label>时间周期</label>
            <select v-model="formTimeframe">
              <option v-for="tf in timeframes" :key="tf" :value="tf">{{ tf }}</option>
            </select>
          </div>
          <div class="form-group">
            <label>入场条件</label>
            <ConditionTreeEditor :node="entryConditionNode" @update="entryConditionNode = $event" />
          </div>
          <div class="form-group">
            <label>出场条件</label>
            <ConditionTreeEditor :node="exitConditionNode" @update="exitConditionNode = $event" />
          </div>
          <div class="form-group">
            <label>执行规则 (JSON)</label>
            <textarea v-model="formExecutionRule" rows="3" placeholder='{"maxPositionSize":100,"maxDailyLoss":500,"slippageTolerance":0.001}'></textarea>
          </div>
        </div>
        <div class="modal-actions">
          <button class="btn-secondary" @click="showForm = false">取消</button>
          <button class="btn-primary" @click="save">保存</button>
        </div>
      </div>
    </div>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th>名称</th>
          <th>交易所</th>
          <th>交易对</th>
          <th>周期</th>
          <th>状态</th>
          <th>版本</th>
          <th>更新时间</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="s in strategies" :key="s.id">
          <td>{{ s.name }}</td>
          <td>{{ getExchangeLabel(s.exchangeId) }}</td>
          <td>{{ s.symbolIds }}</td>
          <td>{{ s.timeframe }}</td>
          <td>
            <span class="status-badge" :style="{ background: statusColors[s.status] }">
              {{ statusLabels[s.status] }}
            </span>
          </td>
          <td>{{ s.version }}</td>
          <td>{{ new Date(s.updatedAtUtc).toLocaleString() }}</td>
          <td class="actions">
            <button
              class="btn-small"
              :class="s.status === 'Active' ? 'btn-warn' : 'btn-ok'"
              :disabled="toggleLoading === s.id || s.status === 'Draft'"
              @click="toggle(s)"
            >
              {{ toggleLoading === s.id ? '...' : s.status === 'Active' ? '禁用' : '启用' }}
            </button>
            <button class="btn-small" :disabled="s.status === 'Active'" @click="openEdit(s)">编辑</button>
            <button class="btn-small" @click="router.push(`/traders/${traderId}/strategies/${s.id}/backtest`)">回测</button>
            <button class="btn-small btn-danger" :disabled="s.status === 'Active'" @click="remove(s.id)">删除</button>
          </td>
        </tr>
        <tr v-if="strategies.length === 0">
          <td colspan="8" class="empty">暂无策略</td>
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
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; }
.table th { color: #94a3b8; font-weight: 600; }
.actions { display: flex; gap: 0.5rem; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.status-badge { display: inline-block; padding: 0.125rem 0.5rem; border-radius: 999px; color: #0f172a; font-size: 0.8rem; font-weight: 600; }
.modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; justify-content: center; align-items: center; z-index: 100; }
.modal { background: #1e293b; padding: 2rem; border-radius: 8px; width: 100%; max-width: 400px; max-height: 85vh; overflow-y: auto; }
.modal-wide { max-width: 720px; }
.modal h3 { margin: 0 0 1rem; color: #e2e8f0; }
.modal input, .modal select, .modal textarea { width: 100%; padding: 0.75rem; border: 1px solid #334155; border-radius: 4px; background: #0f172a; color: #e2e8f0; box-sizing: border-box; font-family: inherit; }
.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem; }
.form-group { display: flex; flex-direction: column; gap: 0.25rem; }
.form-group:has(textarea) { grid-column: 1 / -1; }
.form-group:has(.condition-node) { grid-column: 1 / -1; }
.form-group label { color: #94a3b8; font-size: 0.85rem; }
.modal-actions { display: flex; justify-content: flex-end; gap: 0.5rem; }
</style>
