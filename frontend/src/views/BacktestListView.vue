<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { tradersApi, type Trader } from '../api/traders'
import { strategiesApi, type StrategyBinding } from '../api/strategies'
import { backtestsApi, type BacktestTask } from '../api/backtests'

const router = useRouter()

interface TaskItem {
  traderId: string
  traderName: string
  bindingId: string
  bindingName: string
  task: BacktestTask
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
const bindings = ref<StrategyBinding[]>([])
const formTraderId = ref('')
const formBindingId = ref('')
const formPair = ref('')
const formTimeframe = ref('1h')
const formStartAt = ref('')
const formEndAt = ref('')
const formCapital = ref(1000)
const formSaving = ref(false)
const formError = ref('')

const timeframes = ['1m', '5m', '15m', '30m', '1h', '4h', '1d']

const selectedBinding = computed(() =>
  bindings.value.find(b => b.id === formBindingId.value)
)

const availablePairs = computed(() => {
  const b = selectedBinding.value
  if (!b) return []
  try {
    const parsed = JSON.parse(b.pairs)
    if (Array.isArray(parsed)) return parsed
  } catch {}
  return b.pairs.split(',').map(s => s.trim()).filter(Boolean)
})

onMounted(async () => {
  try {
    const { data: tradersData } = await tradersApi.getAll()
    traders.value = tradersData

    const allTasks: TaskItem[] = []
    for (const trader of tradersData) {
      try {
        const { data: b } = await strategiesApi.getAll(trader.id)
        for (const binding of b) {
          try {
            const { data: backtestTasks } = await backtestsApi.getTasks(binding.strategyId)
            for (const task of backtestTasks) {
              allTasks.push({
                traderId: trader.id,
                traderName: trader.name,
                bindingId: binding.id,
                bindingName: binding.name || binding.strategyId,
                task
              })
            }
          } catch {}
        }
      } catch {}
    }

    allTasks.sort((a, b) => b.task.createdAt.localeCompare(a.task.createdAt))
    tasks.value = allTasks
  } catch {
    error.value = '加载回测任务失败'
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

function openCreate() {
  formTraderId.value = traders.value[0]?.id || ''
  formBindingId.value = ''
  formPair.value = ''
  formTimeframe.value = '1h'
  formCapital.value = 1000
  formError.value = ''
  const now = new Date()
  formEndAt.value = now.toISOString().slice(0, 16)
  const start = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000)
  formStartAt.value = start.toISOString().slice(0, 16)
  loadBindings()
  showForm.value = true
}

async function loadBindings() {
  if (!formTraderId.value) {
    bindings.value = []
    return
  }
  try {
    const { data } = await strategiesApi.getAll(formTraderId.value)
    bindings.value = data
  } catch {
    bindings.value = []
  }
}

function onTraderChange(id: string) {
  formTraderId.value = id
  formBindingId.value = ''
  formPair.value = ''
  loadBindings()
}

function onBindingChange(id: string) {
  formBindingId.value = id
  formPair.value = ''
  const b = selectedBinding.value
  if (b) {
    formTimeframe.value = b.timeframe
    const pairs = availablePairs.value
    if (pairs.length > 0) formPair.value = pairs[0]
  }
}

async function save() {
  formError.value = ''
  const b = selectedBinding.value
  if (!b) { formError.value = '请选择绑定策略'; return }
  if (!formPair.value) { formError.value = '请选择交易对'; return }
  if (!formStartAt.value || !formEndAt.value) { formError.value = '请选择回测时间范围'; return }
  if (!formCapital.value || formCapital.value <= 0) { formError.value = '请输入有效本金'; return }

  formSaving.value = true
  try {
    const startDate = new Date(formStartAt.value)
    const endDate = new Date(formEndAt.value)
    await backtestsApi.start(
      b.strategyId,
      b.exchangeId,
      formPair.value,
      formTimeframe.value,
      startDate.toISOString(),
      endDate.toISOString(),
      formCapital.value
    )
    showForm.value = false
    // Reload tasks after creation
    const allTasks: TaskItem[] = []
    for (const trader of traders.value) {
      try {
        const { data: bs } = await strategiesApi.getAll(trader.id)
        for (const binding of bs) {
          try {
            const { data: backtestTasks } = await backtestsApi.getTasks(binding.strategyId)
            for (const task of backtestTasks) {
              allTasks.push({
                traderId: trader.id,
                traderName: trader.name,
                bindingId: binding.id,
                bindingName: binding.name || binding.strategyId,
                task
              })
            }
          } catch {}
        }
      } catch {}
    }
    allTasks.sort((a, b) => b.task.createdAt.localeCompare(a.task.createdAt))
    tasks.value = allTasks
  } catch (e: any) {
    formError.value = e.response?.data?.message || e.response?.data?.error || '创建回测失败'
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

    <a-modal v-model:visible="showForm" title="新建回测" width="md" :mask-closable="false" @cancel="showForm = false">
      <div class="form-grid">
        <div class="form-group full">
          <label>交易员</label>
          <a-select
            :model-value="formTraderId"
            style="width: 100%"
            @change="(v: any) => onTraderChange(String(v))"
          >
            <a-option v-for="t in traders" :key="t.id" :value="t.id" :label="t.name" />
          </a-select>
        </div>

        <div class="form-group full">
          <label>绑定策略</label>
          <a-select
            :model-value="formBindingId"
            style="width: 100%"
            @change="(v: any) => onBindingChange(String(v))"
          >
            <a-option v-for="b in bindings" :key="b.id" :value="b.id" :label="b.name || b.strategyId" />
          </a-select>
        </div>

        <div class="form-group full">
          <label>交易对</label>
          <a-select
            :model-value="formPair"
            style="width: 100%"
            @change="(v: any) => formPair = String(v)"
          >
            <a-option v-for="p in availablePairs" :key="p" :value="p" :label="p" />
          </a-select>
        </div>

        <div class="form-group">
          <label>时间周期</label>
          <a-select
            :model-value="formTimeframe"
            style="width: 100%"
            @change="(v: any) => formTimeframe = String(v)"
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
            @change="(v: any) => formCapital = Number(v)"
          />
        </div>

        <div class="form-group full">
          <label>开始时间</label>
          <input v-model="formStartAt" type="datetime-local" class="form-input" />
        </div>

        <div class="form-group full">
          <label>结束时间</label>
          <input v-model="formEndAt" type="datetime-local" class="form-input" />
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
        { title: '交易员', dataIndex: 'traderName', width: 120 },
        { title: '绑定策略', dataIndex: 'bindingName', width: 140, ellipsis: true },
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
        pair: t.task.pair,
        timeframe: t.task.timeframe,
        capital: t.task.initialCapital?.toFixed(0) || '-',
        startAt: formatDate(t.task.startAt),
        endAt: formatDate(t.task.endAt)
      }))"
      :pagination="{
        pageSize: 20,
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
.form-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem; }
.form-group.full { grid-column: 1 / -1; }
.form-group { display: flex; flex-direction: column; gap: 0.25rem; }
.form-group label { color: var(--text-muted); font-size: 0.85rem; }
.form-input {
  width: 100%; padding: 0.625rem;
  border: 1px solid var(--glass-border); border-radius: 4px;
  background: rgba(255,255,255,0.35); color: var(--text-primary);
  box-sizing: border-box;
}
.form-error { color: var(--accent-red); font-size: 0.85rem; margin-bottom: 0.5rem; }
</style>
