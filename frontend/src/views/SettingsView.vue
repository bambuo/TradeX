<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { settingsApi, type Setting } from '../api/settings'

const settings = ref<Setting[]>([])
const edited = ref<Record<string, string>>({})
const loading = ref(true)
const saving = ref(false)
const message = ref('')
const messageType = ref<'success' | 'error'>('success')

const groupLabels: Record<string, string> = {
  jwt: 'JWT 认证',
  risk: '风控参数',
  data: '数据配置'
}

const keyLabels: Record<string, string> = {
  'jwt.secret': 'JWT 密钥',
  'jwt.accessToken_expires_minutes': '访问令牌过期时间（分钟）',
  'jwt.refreshToken_expires_days': '刷新令牌过期时间（天）',
  'risk.default_slippage_percent': '默认滑点（%）',
  'risk.max_daily_loss_percent': '每日最大亏损（%）',
  'risk.maxDrawdownPercent': '最大回撤（%）',
  'risk.cooldown_seconds': '冷却时间（秒）',
  'risk.consecutive_loss_limit': '连续亏损上限',
  'data.kline_warmup_days': 'K 线预热天数',
  'data.kline_warmup_interval': 'K 线预热间隔'
}

const readOnlyKeys = ['jwt.secret']

function getGroup(key: string): string {
  return key.split('.')[0]
}

function isReadOnly(key: string): boolean {
  return readOnlyKeys.includes(key)
}

function hasChanges(): boolean {
  return settings.value.some(s => (edited.value[s.key] ?? s.value) !== s.value)
}

async function load() {
  loading.value = true
  try {
    const { data } = await settingsApi.getAll()
    settings.value = data.data ?? []
  } finally {
    loading.value = false
  }
}

async function save() {
  const changedEntries = settings.value
    .filter(s => !isReadOnly(s.key) && (edited.value[s.key] ?? s.value) !== s.value)
    .map(s => ({ key: s.key, value: edited.value[s.key] ?? s.value }))

  if (changedEntries.length === 0) return

  saving.value = true
  message.value = ''
  try {
    await settingsApi.update(changedEntries)
    for (const entry of changedEntries) {
      const setting = settings.value.find(s => s.key === entry.key)
      if (setting) setting.value = entry.value
    }
    message.value = '配置已保存'
    messageType.value = 'success'
    setTimeout(() => { message.value = '' }, 3000)
  } catch {
    message.value = '保存失败'
    messageType.value = 'error'
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="settings-page">
    <header class="page-header">
      <h2>系统配置</h2>
      <button
        class="btn-primary"
        :disabled="!hasChanges() || saving"
        @click="save"
      >
        {{ saving ? '保存中...' : '保存修改' }}
      </button>
    </header>

    <div v-if="message" :class="['msg', messageType]">{{ message }}</div>

    <div v-if="loading">加载中...</div>
    <div v-else class="settings-groups">
      <div
        v-for="group in ['jwt', 'risk', 'data']"
        :key="group"
        class="group-card"
      >
        <h3 class="group-title">{{ groupLabels[group] }}</h3>
        <div class="group-items">
          <div
            v-for="s in settings.filter(s => getGroup(s.key) === group)"
            :key="s.key"
            class="setting-item"
          >
            <div class="setting-info">
              <span class="setting-label">{{ keyLabels[s.key] || s.key }}</span>
              <span class="setting-key">{{ s.key }}</span>
            </div>
            <div class="setting-control">
              <input
                v-if="isReadOnly(s.key)"
                class="input readonly"
                :value="s.value"
                readonly
              />
              <input
                v-else
                class="input"
                :class="{ edited: (edited[s.key] ?? s.value) !== s.value }"
                :value="edited[s.key] ?? s.value"
                @input="(e) => { const t = e.target as HTMLInputElement; edited[s.key] = t.value; }"
              />
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.settings-page { padding: 2rem; max-width: 800px; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.page-header h2 { margin: 0; color: #e2e8f0; }
.btn-primary {
  padding: 0.5rem 1rem; background: #38bdf8; color: #0f172a;
  border: none; border-radius: 4px; cursor: pointer; font-weight: 600;
}
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.msg {
  padding: 0.75rem 1rem; border-radius: 4px; margin-bottom: 1rem; font-size: 0.9rem;
}
.msg.success { background: rgba(34, 197, 94, 0.12); color: #22c55e; border: 1px solid rgba(34, 197, 94, 0.3); }
.msg.error { background: rgba(239, 68, 68, 0.12); color: #ef4444; border: 1px solid rgba(239, 68, 68, 0.3); }
.group-card {
  background: #1e293b; border: 1px solid #334155; border-radius: 8px;
  margin-bottom: 1rem; overflow: hidden;
}
.group-title {
  margin: 0; padding: 0.75rem 1rem; color: #e2e8f0; font-size: 0.95rem;
  background: #0f172a; border-bottom: 1px solid #334155;
}
.group-items { padding: 0.5rem 1rem; }
.setting-item {
  display: flex; align-items: center; justify-content: space-between;
  padding: 0.625rem 0; gap: 1rem;
}
.setting-item + .setting-item { border-top: 1px solid #1e293b; }
.setting-info { flex: 1; min-width: 0; }
.setting-label { display: block; color: #e2e8f0; font-size: 0.875rem; }
.setting-key { display: block; color: #64748b; font-size: 0.75rem; margin-top: 0.125rem; }
.setting-control { flex-shrink: 0; width: 200px; }
.input {
  width: 100%; padding: 0.5rem 0.625rem; border: 1px solid #334155; border-radius: 4px;
  background: #0f172a; color: #e2e8f0; font-size: 0.85rem; box-sizing: border-box;
}
.input.readonly { opacity: 0.5; cursor: not-allowed; }
.input.edited { border-color: #38bdf8; }
</style>
