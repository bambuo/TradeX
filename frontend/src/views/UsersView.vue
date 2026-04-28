<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { usersApi, type User } from '../api/users'

const users = ref<User[]>([])
const loading = ref(true)
const showForm = ref(false)
const formUsername = ref('')
const formPassword = ref('')
const formRole = ref('Operator')

const page = ref(1)
const pageSize = ref(15)

const displayUsers = computed(() => {
  const start = (page.value - 1) * pageSize.value
  return users.value.slice(start, start + pageSize.value)
})

const columns = [
  { title: '用户名', dataIndex: 'userName' },
  { title: '角色', dataIndex: 'role', width: 120 },
  { title: '状态', dataIndex: 'status', width: 100 },
  { title: '创建时间', dataIndex: 'createdAt', width: 200 },
  { title: '操作', dataIndex: 'actions', width: 140 }
]

async function load() {
  loading.value = true
  try {
    const { data } = await usersApi.getAll()
    const list = Array.isArray(data) ? data : (data as any).data ?? []
    users.value = list
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

const tagRoleColors: Record<string, string> = {
  SuperAdmin: 'red', Admin: 'orange', Operator: 'blue', Viewer: ''
}

const tagStatusColors: Record<string, string> = {
  Active: 'green', Disabled: 'gray', PendingMfa: 'purple'
}

onMounted(load)
</script>

<template>
  <div class="users-page">
    <a-card class="header-card">
      <div class="header-row">
        <span class="header-title">用户管理</span>
        <a-button type="primary" @click="openCreate">
          <template #icon><icon-plus /></template>
          创建用户
        </a-button>
      </div>
    </a-card>

    <a-modal v-model:visible="showForm" title="创建用户" width="sm" :mask-closable="false">
      <div class="form-body">
        <a-input v-model="formUsername" placeholder="用户名" />
        <a-input-password v-model="formPassword" placeholder="密码" />
        <a-select
          :model-value="formRole"
          style="width: 100%"
          @change="(v) => formRole = String(v) as 'Admin' | 'Operator' | 'Viewer'"
        >
          <a-option value="Admin" label="Admin" />
          <a-option value="Operator" label="Operator" />
          <a-option value="Viewer" label="Viewer" />
        </a-select>
      </div>
      <template #footer>
        <a-button type="primary" @click="create">
          <template #icon><icon-user /></template>
          创建
        </a-button>
      </template>
    </a-modal>

    <a-table
      :columns="columns"
      :data="displayUsers"
      :loading="loading"
      :pagination="{
        current: page,
        pageSize: pageSize,
        total: users.length,
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
      <template #cell-role="{ record }">
        <a-tag :color="tagRoleColors[record.role] || ''">{{ record.role }}</a-tag>
      </template>
      <template #cell-status="{ record }">
        <a-tag :color="tagStatusColors[record.status] || ''">{{ record.status }}</a-tag>
      </template>
      <template #cell-createdAt="{ record }">
        {{ record.createdAt }}
      </template>
      <template #cell-actions="{ record }">
        <a-select
          :model-value="record.role"
          style="width: 120px"
          @change="(v) => changeRole(record.id, String(v))"
        >
          <a-option value="Admin" label="Admin" />
          <a-option value="Operator" label="Operator" />
          <a-option value="Viewer" label="Viewer" />
        </a-select>
      </template>
    </a-table>
  </div>
</template>

<style scoped>
.users-page { padding: 0; }
.header-card { margin-bottom: 1rem; }
.header-card :deep(.arco-card-body) {
  padding: 0.75rem 1rem;
}
.header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}
.header-title {
  font-size: 1rem;
  font-weight: 600;
  color: var(--text-primary);
}
.form-body { display: flex; flex-direction: column; gap: 0.75rem; }
</style>
