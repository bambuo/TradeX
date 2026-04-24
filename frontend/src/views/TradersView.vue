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

onMounted(loadTraders)
</script>

<template>
  <div class="traders-page">
    <header class="page-header">
      <h2>交易员管理</h2>
      <button class="btn-primary" @click="openCreate">新建交易员</button>
    </header>

    <div v-if="showForm" class="modal-overlay" @click.self="showForm = false">
      <div class="modal">
        <h3>{{ editId ? '编辑交易员' : '新建交易员' }}</h3>
        <input v-model="formName" placeholder="交易员名称" @keyup.enter="save" />
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
          <th>状态</th>
          <th>创建时间</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="t in traders" :key="t.id">
          <td>{{ t.name }}</td>
          <td>{{ t.status }}</td>
          <td>{{ new Date(t.createdAt).toLocaleDateString() }}</td>
          <td class="actions">
            <button class="btn-small" @click="router.push(`/traders/${t.id}/exchanges`)">交易所</button>
            <button class="btn-small" @click="router.push(`/traders/${t.id}/strategies`)">策略</button>
            <button class="btn-small" @click="router.push(`/traders/${t.id}/positions`)">持仓</button>
            <button class="btn-small" @click="router.push(`/traders/${t.id}/orders`)">订单</button>
            <button class="btn-small" @click="openEdit(t)">编辑</button>
            <button class="btn-small btn-danger" @click="remove(t.id)">删除</button>
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
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; }
.table th { color: #94a3b8; font-weight: 600; }
.actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; justify-content: center; align-items: center; z-index: 100; }
.modal { background: #1e293b; padding: 2rem; border-radius: 8px; width: 100%; max-width: 400px; }
.modal h3 { margin: 0 0 1rem; color: #e2e8f0; }
.modal input { width: 100%; padding: 0.75rem; margin-bottom: 1rem; border: 1px solid #334155; border-radius: 4px; background: #0f172a; color: #e2e8f0; box-sizing: border-box; }
.modal-actions { display: flex; justify-content: flex-end; gap: 0.5rem; }
</style>
