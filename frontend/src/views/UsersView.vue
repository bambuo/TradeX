<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { usersApi, type User } from '../api/users'

const users = ref<User[]>([])
const loading = ref(true)
const showForm = ref(false)
const formUsername = ref('')
const formPassword = ref('')
const formRole = ref('Operator')

async function load() {
  loading.value = true
  try {
    const { data } = await usersApi.getAll()
    users.value = data.data ?? []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  formUsername.value = ''
  formPassword.value = ''
  formRole.value = 'Operator'
  showForm.value = true
}

async function create() {
  await usersApi.create({ userName: formUsername.value, password: formPassword.value, role: formRole.value })
  showForm.value = false
  await load()
}

async function changeRole(id: string, role: string) {
  await usersApi.updateRole(id, role)
  await load()
}

const roleColors: Record<string, string> = {
  SuperAdmin: '#ef4444',
  Admin: '#f59e0b',
  Operator: 'var(--accent-blue)',
  Viewer: '#94a3b8'
}

onMounted(load)
</script>

<template>
  <div class="users-page">
    <div class="page-header">
      <h2>用户管理</h2>
      <AppButton variant="primary" icon="plus" @click="openCreate">创建用户</AppButton>
    </div>

    <AppModal v-model="showForm" title="创建用户" width="sm">
      <input v-model="formUsername" placeholder="用户名" class="input" />
      <input v-model="formPassword" type="password" placeholder="密码" class="input" />
      <select v-model="formRole" class="input">
        <option value="Admin">Admin</option>
        <option value="Operator">Operator</option>
        <option value="Viewer">Viewer</option>
      </select>
      <template #footer>
        <AppButton icon="close" @click="showForm = false">取消</AppButton>
        <AppButton variant="primary" icon="user" @click="create">创建</AppButton>
      </template>
    </AppModal>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th>用户名</th>
          <th>角色</th>
          <th>状态</th>
          <th>创建时间</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="u in users" :key="u.id">
          <td>{{ u.userName }}</td>
          <td><span class="role-badge" :style="{ color: roleColors[u.role] }">{{ u.role }}</span></td>
          <td>{{ u.status }}</td>
          <td>{{ new Date(u.createdAt).toLocaleDateString() }}</td>
          <td class="actions">
            <select class="role-select" :value="u.role" @change="(e) => changeRole(u.id, (e.target as HTMLSelectElement).value)">
              <option value="Admin">Admin</option>
              <option value="Operator">Operator</option>
              <option value="Viewer">Viewer</option>
            </select>
          </td>
        </tr>
        <tr v-if="users.length === 0">
          <td colspan="5" class="empty">暂无用户</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.users-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.page-header h2 { margin: 0; color: var(--text-primary); }
.btn-primary { padding: 0.5rem 1rem; background: var(--accent-blue); color: var(--text-primary); border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: var(--text-primary); border: 1px solid var(--glass-border-strong); border-radius: 4px; cursor: pointer; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); }
.table th { color: var(--text-muted); font-weight: 600; }
.role-badge { font-weight: 600; font-size: 0.85rem; }
.actions { display: flex; gap: 0.5rem; }
.role-select { padding: 0.25rem; background: rgba(255,255,255,0.35); color: var(--text-primary); border: 1px solid var(--glass-border); border-radius: 4px; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.input { width: 100%; padding: 0.6rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-primary); box-sizing: border-box; }
</style>
