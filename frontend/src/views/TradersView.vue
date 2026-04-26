<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { tradersApi, type Trader } from '../api/traders'

const router = useRouter()

const traders = ref<Trader[]>([])
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const formName = ref('')

const statusLabels: Record<string, string> = {
  Active: '活跃',
  Disabled: '已禁用',
  Deleted: '已删除'
}

const statusColors: Record<string, string> = {
  Active: '#22c55e',
  Disabled: '#f59e0b',
  Deleted: '#ef4444'
}

const statusByCode: Record<number, string> = {
  0: 'Active',
  1: 'Disabled',
  2: 'Deleted'
}

async function loadTraders() {
  loading.value = true
  try {
    const { data } = await tradersApi.getAll()
    traders.value = data
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editId.value = null
  formName.value = ''
  showForm.value = true
}

function openEdit(trader: Trader) {
  editId.value = trader.id
  formName.value = trader.name
  showForm.value = true
}

async function save() {
  if (editId.value) {
    await tradersApi.update(editId.value, formName.value)
  } else {
    await tradersApi.create(formName.value)
  }
  showForm.value = false
  await loadTraders()
}

async function remove(id: string) {
  await tradersApi.delete(id)
  await loadTraders()
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
  return statusLabels[normalized] ?? (normalized || '-')
}

function getStatusColor(status: unknown): string {
  const normalized = normalizeStatus(status)
  return statusColors[normalized] ?? '#94a3b8'
}

onMounted(loadTraders)
</script>

<template>
  <div class="traders-page">
    <header class="page-header">
      <h2>交易员管理</h2>
      <AppButton variant="primary" icon="plus" @click="openCreate">新建交易员</AppButton>
    </header>

    <AppModal v-model="showForm" :title="editId ? '编辑交易员' : '新建交易员'" width="sm">
      <input v-model="formName" placeholder="交易员名称" class="input" @keyup.enter="save" />
      <template #footer>
        <AppButton icon="close" @click="showForm = false">取消</AppButton>
        <AppButton variant="primary" icon="save" @click="save">保存</AppButton>
      </template>
    </AppModal>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th>名称</th>
          <th>状态</th>
          <th>创建时间</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="t in traders" :key="t.id">
          <td>{{ t.name }}</td>
          <td>
            <span class="status-badge" :style="{ background: getStatusColor(t.status) }">
              {{ getStatusLabel(t.status) }}
            </span>
          </td>
          <td>{{ new Date(t.createdAt).toLocaleDateString() }}</td>
          <td class="actions">
            <AppButton size="sm" icon="strategy" @click="router.push(`/traders/${t.id}/strategies`)">策略</AppButton>
            <AppButton size="sm" icon="positions" @click="router.push(`/traders/${t.id}/positions`)">持仓</AppButton>
            <AppButton size="sm" icon="orders" @click="router.push(`/traders/${t.id}/orders`)">订单</AppButton>
            <AppButton size="sm" icon="edit" @click="openEdit(t)">编辑</AppButton>
            <AppButton size="sm" variant="danger" icon="trash" @click="remove(t.id)">删除</AppButton>
          </td>
        </tr>
        <tr v-if="traders.length === 0">
          <td colspan="4" class="empty">暂无交易员</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.traders-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.page-header h2 { margin: 0; color: #e2e8f0; }
.btn-primary { padding: 0.5rem 1rem; background: #38bdf8; color: #0f172a; border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; }
.btn-small { padding: 0.25rem 0.75rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
.btn-danger { color: #ef4444; border-color: #ef4444; background: transparent; }
.status-badge { display: inline-block; padding: 0.125rem 0.5rem; border-radius: 999px; color: #0f172a; font-size: 0.8rem; font-weight: 600; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; }
.table th { color: #94a3b8; font-weight: 600; }
.actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.input { width: 100%; padding: 0.75rem; border: 1px solid #334155; border-radius: 4px; background: #0f172a; color: #e2e8f0; box-sizing: border-box; }
</style>
