<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { notificationChannelsApi, type NotificationChannel } from '../api/notificationChannels'
import AppSelect from '../components/AppSelect.vue'

const channels = ref<NotificationChannel[]>([])
const loading = ref(true)
const showForm = ref(false)
const formName = ref('')
const formType = ref('Telegram')
const formBotToken = ref('')
const formChatId = ref('')
const formWebhookUrl = ref('')
const formHost = ref('')
const formPort = ref('')
const formUsername = ref('')
const formPassword = ref('')
const formFromAddress = ref('')
const formToAddress = ref('')
const testing = ref<string | null>(null)

async function load() {
  loading.value = true
  try {
    const { data } = await notificationChannelsApi.getAll()
    channels.value = data.data ?? []
  } finally {
    loading.value = false
  }
}

function openCreate() {
  formName.value = ''
  formType.value = 'Telegram'
  formBotToken.value = ''
  formChatId.value = ''
  formWebhookUrl.value = ''
  showForm.value = true
}

function getConfig(): Record<string, string> {
  if (formType.value === 'Telegram') return { botToken: formBotToken.value, chatId: formChatId.value }
  if (formType.value === 'Discord') return { webhookUrl: formWebhookUrl.value }
  return { host: formHost.value, port: formPort.value, userName: formUsername.value, password: formPassword.value, fromAddress: formFromAddress.value, toAddress: formToAddress.value }
}

async function save() {
  await notificationChannelsApi.create({ name: formName.value, type: formType.value, config: getConfig() })
  showForm.value = false
  await load()
}

async function remove(id: string) {
  await notificationChannelsApi.delete(id)
  await load()
}

async function test(id: string) {
  testing.value = id
  try {
    await notificationChannelsApi.test(id)
  } finally {
    testing.value = null
  }
}

onMounted(load)
</script>

<template>
  <div class="notif-page">
    <div class="page-header">
      <h2>通知渠道</h2>
      <AppButton variant="primary" icon="plus" @click="openCreate">添加渠道</AppButton>
    </div>

    <AppModal v-model="showForm" title="添加通知渠道" width="md">
      <input v-model="formName" placeholder="渠道名称" class="input" />
      <AppSelect
        :options="[
          { label: 'Telegram', value: 'Telegram' },
          { label: 'Discord', value: 'Discord' },
          { label: 'Email', value: 'Email' },
        ]"
        :model-value="formType"
        full
        form
        @update:model-value="(v: string | number) => formType = String(v)"
      />

      <template v-if="formType === 'Telegram'">
        <input v-model="formBotToken" placeholder="Bot Token" class="input" />
        <input v-model="formChatId" placeholder="Chat ID" class="input" />
      </template>
      <template v-else-if="formType === 'Discord'">
        <input v-model="formWebhookUrl" placeholder="Webhook URL" class="input" />
      </template>
      <template v-else>
        <input v-model="formHost" placeholder="SMTP Host" class="input" />
        <input v-model="formPort" placeholder="Port" class="input" />
        <input v-model="formUsername" placeholder="Username" class="input" />
        <input v-model="formPassword" type="password" placeholder="Password" class="input" />
        <input v-model="formFromAddress" placeholder="From Address" class="input" />
        <input v-model="formToAddress" placeholder="To Address" class="input" />
      </template>

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
          <th>类型</th>
          <th>状态</th>
          <th>默认</th>
          <th>最近测试</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="c in channels" :key="c.id">
          <td>{{ c.name }}</td>
          <td><span class="badge">{{ c.type }}</span></td>
          <td>{{ c.status }}</td>
          <td>{{ c.isDefault ? '✓' : '-' }}</td>
          <td>{{ c.lastTestedAt ? new Date(c.lastTestedAt).toLocaleString() : '-' }}</td>
          <td class="actions">
            <AppButton size="sm" icon="test" :disabled="testing === c.id" @click="test(c.id)">{{ testing === c.id ? '测试中...' : '测试' }}</AppButton>
            <AppButton size="sm" variant="danger" icon="trash" @click="remove(c.id)">删除</AppButton>
          </td>
        </tr>
        <tr v-if="channels.length === 0">
          <td colspan="6" class="empty">暂无通知渠道</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.notif-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.page-header h2 { margin: 0; color: var(--text-primary); }
.btn-primary { padding: 0.5rem 1rem; background: var(--accent-blue); color: var(--text-primary); border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: var(--text-primary); border: 1px solid var(--glass-border-strong); border-radius: 4px; cursor: pointer; }
.btn-small { padding: 0.25rem 0.75rem; background: #334155; color: var(--text-primary); border: 1px solid var(--glass-border-strong); border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
.btn-small:disabled { opacity: 0.5; }
.btn-danger { color: var(--accent-red); border-color: var(--accent-red); background: transparent; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); }
.table th { color: var(--text-muted); font-weight: 600; }
.badge { padding: 0.15rem 0.5rem; border-radius: 4px; font-size: 0.8rem; background: rgba(56,189,248,0.1); color: var(--accent-blue); }
.actions { display: flex; gap: 0.5rem; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.input { width: 100%; padding: 0.6rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-primary); box-sizing: border-box; }
</style>
