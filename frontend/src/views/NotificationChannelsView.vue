<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { notificationChannelsApi, type NotificationChannel } from '../api/notificationChannels'

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
const toast = ref({ message: '', type: '' as 'success' | 'error' })

const typeMeta: Record<string, { label: string; icon: string; color: string; bg: string }> = {
  Telegram: { label: 'Telegram', icon: '✈', color: '#4f7ec9', bg: 'rgba(79,126,201,0.10)' },
  Discord: { label: 'Discord', icon: '◆', color: '#8b5cf6', bg: 'rgba(139,92,246,0.10)' },
  Email: { label: 'Email', icon: '◎', color: '#b8893a', bg: 'rgba(184,137,58,0.10)' }
}

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

function showToast(message: string, type: 'success' | 'error') {
  toast.value = { message, type }
  setTimeout(() => { toast.value.message = '' }, 4000)
}

async function remove(id: string) {
  await notificationChannelsApi.delete(id)
  await load()
}

async function test(id: string) {
  testing.value = id
  try {
    await notificationChannelsApi.test(id)
    showToast('测试消息已发送', 'success')
  } catch (e: unknown) {
    const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message || '测试发送失败'
    showToast(msg, 'error')
  } finally {
    testing.value = null
  }
}

async function toggleStatus(id: string) {
  await notificationChannelsApi.toggleStatus(id)
  await load()
}

onMounted(load)
</script>

<template>
  <div class="notif-page">
    <header class="page-header">
      <h2>通知渠道</h2>
      <a-button type="primary" @click="openCreate">
        <template #icon><icon-plus /></template>
        添加渠道
      </a-button>
    </header>

    <Transition name="toast-fade">
      <div v-if="toast.message" class="toast" :class="`toast--${toast.type}`">
        {{ toast.message }}
      </div>
    </Transition>

    <a-modal v-model:visible="showForm" title="添加通知渠道" width="md" :mask-closable="false">
      <div class="form-body">
        <div class="form-group">
          <label class="form-label">渠道名称</label>
          <a-input v-model="formName" placeholder="输入渠道名称" />
        </div>

        <div class="form-group">
          <label class="form-label">渠道类型</label>
          <a-select
            :model-value="formType"
            style="width: 100%"
            @change="(v) => formType = String(v)"
          >
            <a-option value="Telegram" label="Telegram" />
            <a-option value="Discord" label="Discord" />
            <a-option value="Email" label="Email" />
          </a-select>
        </div>

        <template v-if="formType === 'Telegram'">
          <div class="form-section">Telegram 配置</div>
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">Bot Token</label>
              <a-input v-model="formBotToken" placeholder="输入 Bot Token" />
            </div>
            <div class="form-group">
              <label class="form-label">Chat ID</label>
              <a-input v-model="formChatId" placeholder="输入 Chat ID" />
            </div>
          </div>
        </template>

        <template v-else-if="formType === 'Discord'">
          <div class="form-section">Discord 配置</div>
          <div class="form-group">
            <label class="form-label">Webhook URL</label>
            <a-input v-model="formWebhookUrl" placeholder="输入 Webhook URL" />
          </div>
        </template>

        <template v-else>
          <div class="form-section">SMTP 配置</div>
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">SMTP Host</label>
              <a-input v-model="formHost" placeholder="smtp.example.com" />
            </div>
            <div class="form-group">
              <label class="form-label">端口</label>
              <a-input v-model="formPort" placeholder="587" />
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">用户名</label>
              <a-input v-model="formUsername" placeholder="输入用户名" />
            </div>
            <div class="form-group">
              <label class="form-label">密码</label>
              <a-input-password v-model="formPassword" placeholder="输入密码" />
            </div>
          </div>
          <div class="form-row">
            <div class="form-group">
              <label class="form-label">发件地址</label>
              <a-input v-model="formFromAddress" placeholder="from@example.com" />
            </div>
            <div class="form-group">
              <label class="form-label">收件地址</label>
              <a-input v-model="formToAddress" placeholder="to@example.com" />
            </div>
          </div>
        </template>
      </div>

      <template #footer>
        <a-button type="primary" @click="save">
          <template #icon><icon-save /></template>
          保存
        </a-button>
      </template>
    </a-modal>

    <div v-if="loading" class="loading">加载中...</div>

    <div v-else-if="channels.length === 0" class="empty">暂无通知渠道</div>

    <div v-else class="card-grid">
      <div
        v-for="c in channels"
        :key="c.id"
        class="channel-card"
        :style="{ borderTopColor: (typeMeta[c.type] ?? typeMeta.Telegram).color }"
      >
        <div class="card-header">
          <div class="type-icon" :style="{ background: (typeMeta[c.type] ?? typeMeta.Telegram).bg, color: (typeMeta[c.type] ?? typeMeta.Telegram).color }">
            {{ (typeMeta[c.type] ?? typeMeta.Telegram).icon }}
          </div>
          <div class="card-title-area">
            <h3>{{ c.name }}</h3>
            <span
              class="type-badge"
              :style="{ background: (typeMeta[c.type] ?? typeMeta.Telegram).bg, color: (typeMeta[c.type] ?? typeMeta.Telegram).color }"
            >
              {{ (typeMeta[c.type] ?? typeMeta.Telegram).label }}
            </span>
          </div>
          <div class="card-header-actions">
            <span v-if="c.isDefault" class="default-badge">默认</span>
            <a-switch :model-value="c.status === 'Enabled'" @change="() => toggleStatus(c.id)" />
          </div>
        </div>

        <div class="card-body">
          <div class="info-row">
            <span class="info-label">状态</span>
            <a-tag :color="c.status === 'Enabled' ? 'green' : ''">{{ c.status === 'Enabled' ? '启用' : '禁用' }}</a-tag>
          </div>
          <div class="info-row">
            <span class="info-label">最近测试</span>
            <span class="info-value">{{ c.lastTestedAt ? new Date(c.lastTestedAt).toLocaleString() : '-' }}</span>
          </div>
          <div class="info-row">
            <span class="info-label">创建时间</span>
            <span class="info-value">{{ new Date(c.createdAt).toLocaleString() }}</span>
          </div>
        </div>

        <div class="card-footer">
          <a-button size="small" :loading="testing === c.id" @click="test(c.id)">
            <template #icon><icon-check-circle /></template>
            {{ testing === c.id ? '测试中...' : '测试' }}
          </a-button>
          <a-button size="small" status="danger" @click="remove(c.id)">
            <template #icon><icon-delete /></template>
            删除
          </a-button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.notif-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.5rem; }
.page-header h2 { margin: 0; color: var(--text-primary); }
.loading { text-align: center; color: var(--text-muted); padding: 3rem; font-size: 0.95rem; }
.empty { text-align: center; color: var(--text-muted); padding: 3rem; font-size: 0.95rem; }

.toast {
  position: fixed;
  top: 1rem;
  left: 50%;
  transform: translateX(-50%);
  z-index: 1200;
  padding: 0.75rem 1.5rem;
  font-size: 0.85rem;
  backdrop-filter: blur(24px) saturate(180%);
  box-shadow: 0 18px 50px rgba(2, 6, 23, 0.28);
  pointer-events: none;
  border-radius: 6px;
}
.toast--success {
  background: rgba(21, 128, 61, 0.85);
  border: 1px solid rgba(34, 197, 94, 0.5);
  color: #fff;
}
.toast--error {
  background: rgba(185, 28, 28, 0.85);
  border: 1px solid rgba(239, 68, 68, 0.5);
  color: #fff;
}
.toast-fade-enter-active, .toast-fade-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}
.toast-fade-enter-from, .toast-fade-leave-to {
  opacity: 0;
  transform: translateY(-0.5rem);
}

.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
  gap: 1rem;
  align-items: start;
}

.channel-card {
  background: var(--card-bg, #fff);
  border: 1px solid var(--glass-border);
  border-top: 3px solid;
  border-radius: 8px;
  overflow: hidden;
  transition: box-shadow 0.2s ease, transform 0.2s ease;
}
.channel-card:hover {
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.06);
  transform: translateY(-2px);
}

.card-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem 1.25rem 0.5rem;
}

.type-icon {
  width: 36px;
  height: 36px;
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  font-size: 1.1rem;
  flex-shrink: 0;
}

.card-title-area {
  flex: 1;
  min-width: 0;
}
.card-title-area h3 {
  margin: 0 0 0.25rem;
  font-size: 1rem;
  color: var(--text-primary);
  font-weight: 600;
  line-height: 1.3;
}

.card-header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  align-self: flex-start;
}

.default-badge {
  font-size: 0.72rem;
  font-weight: 600;
  padding: 0.1rem 0.4rem;
  border-radius: 4px;
  background: rgba(79, 126, 201, 0.10);
  color: var(--accent-blue);
}

.type-badge {
  display: inline-block;
  padding: 0.1rem 0.5rem;
  border-radius: 4px;
  font-size: 0.72rem;
  font-weight: 600;
  line-height: 1.5;
}

.card-body {
  padding: 0.5rem 1.25rem 0.75rem;
}

.info-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.3rem 0;
  font-size: 0.85rem;
}

.info-label {
  color: var(--text-muted);
  flex-shrink: 0;
}

.info-value {
  color: var(--text-primary);
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.4rem;
}

.card-footer {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem 1.25rem 1rem;
  border-top: 1px solid var(--glass-border);
}
.card-footer :deep(.arco-btn) {
  flex: 1;
}

.form-body {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
.form-section {
  font-weight: 600;
  color: var(--text-primary);
  font-size: 0.9rem;
  padding-top: 0.25rem;
  border-top: 1px solid var(--glass-border);
}
.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.form-label {
  font-size: 0.8rem;
  color: var(--text-muted);
  font-weight: 500;
}
.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
}
@media (max-width: 600px) {
  .form-row { grid-template-columns: 1fr; }
}
</style>
