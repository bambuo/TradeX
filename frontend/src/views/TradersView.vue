<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { tradersApi, type Trader, type TraderStats } from '../api/traders'

const router = useRouter()

const traders = ref<Trader[]>([])
const traderStats = ref<Record<string, TraderStats>>({})
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const formName = ref('')
const formAvatarColor = ref('')
const formAvatarUrl = ref('')
const formStyle = ref('')
const uploading = ref(false)
const formError = ref('')
const avatarInput = ref<HTMLInputElement | null>(null)
const avatarPalette = ['#4f7ec9', '#528a60', '#b8893a', '#bf4848', '#7c6eb0', '#c97b4a', '#4a9ea0', '#b06e8a']

const styleOptions = ['稳健型', '激进型', '平衡型', '灵活型']
const styleColors: Record<string, string> = {
  '稳健型': 'var(--accent-green)',
  '激进型': 'var(--accent-red)',
  '平衡型': 'var(--accent-amber)',
  '灵活型': 'var(--accent-blue)'
}

const statusLabels: Record<string, string> = {
  Active: '在岗',
  Disabled: '离岗',
  Deleted: '已删除'
}

const statusColors: Record<string, string> = {
  Active: 'var(--accent-green)',
  Disabled: 'var(--text-muted)',
  Deleted: 'var(--accent-red)'
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
    await Promise.all(data.map(t =>
      tradersApi.getStats(t.id).then(r => { traderStats.value[t.id] = r.data }).catch(() => {})
    ))
  } finally {
    loading.value = false
  }
}

function openCreate() {
  editId.value = null
  formName.value = ''
  formAvatarColor.value = avatarPalette[Math.floor(Math.random() * avatarPalette.length)]
  formAvatarUrl.value = ''
  formStyle.value = ''
  formError.value = ''
  showForm.value = true
}

function openEdit(trader: Trader) {
  editId.value = trader.id
  formName.value = trader.name
  formAvatarColor.value = trader.avatarColor || getAvatarColorFromName(trader.name)
  formAvatarUrl.value = trader.avatarUrl || ''
  formStyle.value = trader.style || ''
  formError.value = ''
  showForm.value = true
}

async function save() {
  formError.value = ''
  const data: Record<string, unknown> = { name: formName.value }
  if (formAvatarColor.value) data.avatarColor = formAvatarColor.value
  if (formStyle.value) data.style = formStyle.value
  try {
    if (editId.value) {
      await tradersApi.update(editId.value, data)
    } else {
      await tradersApi.create(data as { name: string; avatarColor?: string; style?: string })
    }
    showForm.value = false
    await loadTraders()
  } catch (e: any) {
    formError.value = e.response?.data?.message || e.response?.data?.error || '保存失败'
  }
}

async function handleAvatarUpload(event: Event) {
  const input = event.target as HTMLInputElement
  const file = input.files?.[0]
  if (!file || !editId.value) return
  uploading.value = true
  try {
    const { data } = await tradersApi.uploadAvatar(editId.value, file)
    formAvatarUrl.value = data.avatarUrl
    await loadTraders()
  } finally {
    uploading.value = false
    input.value = ''
  }
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
  return statusColors[normalized] ?? 'var(--text-muted)'
}

function getAvatarColorFromName(name: string): string {
  let hash = 0
  for (let i = 0; i < name.length; i++) hash = name.charCodeAt(i) + ((hash << 5) - hash)
  return avatarPalette[Math.abs(hash) % avatarPalette.length]
}

function getAvatarColor(trader: { name: string; avatarColor?: string }): string {
  if (trader.avatarColor) return trader.avatarColor
  return getAvatarColorFromName(trader.name)
}

async function toggleStatus(trader: Trader) {
  const newStatus = normalizeStatus(trader.status) === 'Active' ? 'Disabled' : 'Active'
  await tradersApi.update(trader.id, { status: newStatus })
  await loadTraders()
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
      <div class="form-preview">
        <div
          v-if="!formAvatarUrl"
          class="form-avatar-clickable"
          :style="{ background: formAvatarColor + '18', color: formAvatarColor }"
          @click="avatarInput?.click()"
          :title="editId ? '点击上传头像' : ''"
        >
          <span class="avatar-letter">{{ (formName || '?').charAt(0).toUpperCase() }}</span>
          <div class="avatar-overlay">
            <AppIcon name="edit" :size="14" />
          </div>
        </div>
        <div v-else class="form-avatar-img-wrap" @click="avatarInput?.click()" :title="editId ? '点击更换头像' : ''">
          <img :src="formAvatarUrl" alt="" class="form-avatar-img" />
          <div class="avatar-overlay">
            <AppIcon name="edit" :size="14" />
          </div>
        </div>
        <input ref="avatarInput" type="file" accept="image/*" class="hidden-input" @change="handleAvatarUpload" />
        <div class="form-meta">
          <strong>{{ formName || '新交易员' }}</strong>
          <span>{{ editId ? '点击头像上传图片' : '设置名称和头像' }}</span>
        </div>
      </div>
      <div class="form-field">
        <label class="form-label">名称</label>
        <input v-model="formName" placeholder="输入交易员名称" class="input" @keyup.enter="save" />
      </div>
      <div v-if="formError" class="form-error">{{ formError }}</div>
      <div class="form-field">
        <label class="form-label">交易风格</label>
        <div class="style-options">
          <button
            v-for="s in styleOptions"
            :key="s"
            class="style-chip"
            :class="{ active: formStyle === s }"
            :style="{
              '--chip-color': styleColors[s] || 'var(--text-muted)',
              '--chip-bg': (styleColors[s] || 'var(--text-muted)') + '12',
              '--chip-border': (styleColors[s] || 'var(--text-muted)') + '28'
            }"
            @click="formStyle = s"
          >{{ s }}</button>
          <button v-if="formStyle" class="style-chip style-clear" @click="formStyle = ''">清除</button>
        </div>
      </div>
      <template #footer>
        <AppButton icon="close" @click="showForm = false">取消</AppButton>
        <AppButton variant="primary" icon="save" :disabled="uploading" @click="save">保存</AppButton>
      </template>
    </AppModal>

    <div v-if="loading" class="loading-state">加载中...</div>

    <div v-else-if="traders.length === 0" class="empty-state">
      <strong>暂无交易员</strong>
      <span>点击上方按钮创建第一个交易员</span>
    </div>

    <div v-else class="trader-grid">
      <div v-for="t in traders" :key="t.id" class="trader-card" :style="{ borderTopColor: getAvatarColor(t) }">
        <div class="card-header">
          <img v-if="t.avatarUrl" :src="t.avatarUrl" alt="" class="trader-avatar-img" />
          <div v-else class="trader-avatar" :style="{ background: getAvatarColor(t) + '18', color: getAvatarColor(t) }">{{ t.name.charAt(0).toUpperCase() }}</div>
          <div class="trader-meta">
            <strong class="trader-name">{{ t.name }}</strong>
            <span class="trader-status">
              <span class="status-dot" :style="{ background: getStatusColor(t.status) }" />
              <span :style="{ color: getStatusColor(t.status) }">{{ getStatusLabel(t.status) }}</span>
            </span>
          </div>
          <div class="card-header-actions">
            <AppButton size="sm" variant="ghost" icon="edit" title="编辑名称" @click="openEdit(t)" />
            <label class="switch" :title="normalizeStatus(t.status) === 'Active' ? '离岗' : '在岗'">
              <input type="checkbox" :checked="normalizeStatus(t.status) === 'Active'" @change="toggleStatus(t)" />
              <span class="switch-slider" />
            </label>
          </div>
        </div>

        <div v-if="t.style" class="card-tag-row">
          <span class="style-tag" :style="{ background: (styleColors[t.style] || 'var(--text-muted)') + '14', color: styleColors[t.style] || 'var(--text-muted)', borderColor: (styleColors[t.style] || 'var(--text-muted)') + '28' }">{{ t.style }}</span>
        </div>

        <div v-if="traderStats[t.id]" class="card-stats">
          <div class="stat-item">
            <span class="stat-num">{{ traderStats[t.id].totalTrades }}</span>
            <span class="stat-label">交易次数</span>
          </div>
          <div class="stat-item">
            <span class="stat-num" :class="traderStats[t.id].winRate >= 50 ? 'num-up' : 'num-down'">{{ traderStats[t.id].winRate }}%</span>
            <span class="stat-label">胜率</span>
          </div>
          <div class="stat-item">
            <span class="stat-num" :class="traderStats[t.id].profitLossRatio >= 1.5 ? 'num-up' : (traderStats[t.id].profitLossRatio >= 1 ? 'num-neutral' : 'num-down')">{{ traderStats[t.id].profitLossRatio.toFixed(2) }}</span>
            <span class="stat-label">盈亏比</span>
          </div>
          <div class="stat-item">
            <span class="stat-num" :class="traderStats[t.id].sharpeRatio >= 1 ? 'num-up' : 'num-down'">{{ traderStats[t.id].sharpeRatio.toFixed(2) }}</span>
            <span class="stat-label">夏普率</span>
          </div>
        </div>

        <div class="card-actions">
          <AppButton size="sm" icon="strategy" @click="router.push(`/traders/${t.id}/strategies`)">策略部署</AppButton>
          <AppButton size="sm" icon="positions" @click="router.push(`/traders/${t.id}/positions`)">持仓</AppButton>
          <AppButton size="sm" icon="orders" @click="router.push(`/traders/${t.id}/orders`)">订单</AppButton>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.traders-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1.25rem; }
.page-header h2 { margin: 0; color: var(--text-primary); }
.loading-state, .empty-state { text-align: center; color: var(--text-muted); padding: 3rem 1rem; display: flex; flex-direction: column; gap: 0.35rem; }
.empty-state strong { color: var(--text-secondary); }

.trader-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 1rem;
}

.trader-card {
  background: rgba(255, 255, 255, 0.72);
  border: 1px solid rgba(0, 0, 0, 0.06);
  border-top: 3px solid transparent;
  border-radius: 6px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.card-header {
  display: flex;
  align-items: center;
  gap: 0.85rem;
}

.trader-avatar {
  width: 3rem;
  height: 3rem;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 1.2rem;
  flex-shrink: 0;
}

.trader-avatar-img {
  width: 3rem;
  height: 3rem;
  border-radius: 50%;
  object-fit: cover;
  flex-shrink: 0;
}

.trader-meta {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
  min-width: 0;
}

.trader-name {
  font-size: 1rem;
  color: var(--text-primary);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.trader-status {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  font-size: 0.8rem;
}

.status-dot {
  width: 0.45rem;
  height: 0.45rem;
  border-radius: 50%;
  flex-shrink: 0;
}

.card-header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.switch {
  position: relative;
  display: inline-block;
  width: 36px;
  height: 20px;
  cursor: pointer;
}
.switch input {
  display: none;
}
.switch-slider {
  position: absolute;
  inset: 0;
  background: var(--glass-border-strong);
  border-radius: 999px;
  transition: background 0.2s ease;
}
.switch-slider::before {
  content: '';
  position: absolute;
  width: 16px;
  height: 16px;
  left: 2px;
  bottom: 2px;
  background: #fff;
  border-radius: 50%;
  transition: transform 0.2s ease;
}
.switch input:checked + .switch-slider {
  background: #4ade80;
}
.switch input:checked + .switch-slider::before {
  transform: translateX(16px);
}

.stat-item .stat-label { font-size: 0.7rem; color: var(--text-muted); white-space: nowrap; }

.card-stats {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 0.5rem;
  padding: 0.75rem 0;
}

.stat-item {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.15rem;
}

.stat-num { font-size: 1.05rem; font-weight: 700; color: var(--text-primary); }
.stat-num.num-up { color: var(--accent-green); }
.stat-num.num-down { color: var(--accent-red); }
.stat-num.num-neutral { color: var(--text-secondary); }

.card-actions {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.5rem;
  padding-top: 0.75rem;
  border-top: 1px solid rgba(0, 0, 0, 0.04);
}

.card-actions :deep(.app-button) {
  width: 100%;
}

.form-preview {
  display: flex;
  align-items: center;
  gap: 0.85rem;
  padding: 0.75rem;
  margin-bottom: 1rem;
  border-radius: 6px;
  background: rgba(0, 0, 0, 0.02);
  border: 1px solid rgba(0, 0, 0, 0.04);
}

.form-avatar {
  width: 2.6rem;
  height: 2.6rem;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 1.1rem;
  flex-shrink: 0;
}

.form-avatar-clickable {
  width: 3.2rem;
  height: 3.2rem;
  border-radius: 50%;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 1.2rem;
  flex-shrink: 0;
  cursor: pointer;
  position: relative;
  overflow: hidden;
}

.avatar-letter { position: relative; z-index: 1; }

.avatar-overlay {
  position: absolute;
  inset: 0;
  background: rgba(0, 0, 0, 0.35);
  display: flex;
  align-items: center;
  justify-content: center;
  color: #fff;
  opacity: 0;
  transition: opacity 0.15s;
}

.form-avatar-clickable:hover .avatar-overlay { opacity: 1; }

.form-avatar-img-wrap {
  width: 3.2rem;
  height: 3.2rem;
  border-radius: 50%;
  flex-shrink: 0;
  cursor: pointer;
  position: relative;
  overflow: hidden;
}

.form-avatar-img-wrap:hover .avatar-overlay { opacity: 1; }

.form-avatar-img {
  width: 100%;
  height: 100%;
  object-fit: cover;
  border-radius: 50%;
}

.hidden-input { display: none; }

.card-tag-row {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  flex-wrap: wrap;
  min-height: 0;
}

.style-tag {
  display: inline-block;
  padding: 0.15rem 0.5rem;
  border-radius: 4px;
  border: 1px solid;
  font-size: 0.72rem;
  font-weight: 600;
}

.style-options {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
  align-items: center;
}

.style-chip {
  padding: 0.35rem 0.7rem;
  border-radius: 6px;
  border: 1px solid var(--chip-border);
  background: var(--chip-bg);
  color: var(--chip-color);
  cursor: pointer;
  font-size: 0.8rem;
  font-weight: 600;
  transition: box-shadow 0.15s;
}

.style-chip.active {
  box-shadow: 0 0 0 2px var(--chip-color);
}

.style-clear {
  color: var(--text-muted);
  background: transparent;
  border-color: transparent;
  font-weight: 400;
}

.form-error {
  padding: 0.5rem 0.75rem;
  border-radius: 6px;
  background: rgba(191, 72, 72, 0.06);
  border: 1px solid rgba(191, 72, 72, 0.18);
  color: var(--accent-red);
  font-size: 0.82rem;
}

.form-meta {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 0.15rem;
}

.form-meta strong {
  color: var(--text-primary);
  font-size: 0.95rem;
}

.form-meta span {
  color: var(--text-muted);
  font-size: 0.78rem;
}

.form-field {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.form-label {
  color: var(--text-muted);
  font-size: 0.8rem;
}

.color-picker {
  display: flex;
  gap: 0.5rem;
  flex-wrap: wrap;
}

.color-chip {
  width: 1.6rem;
  height: 1.6rem;
  border-radius: 50%;
  border: 2px solid transparent;
  cursor: pointer;
  transition: border-color 0.15s, transform 0.15s;
}

.color-chip:hover {
  transform: scale(1.15);
}

.color-chip.active {
  border-color: var(--text-primary);
  transform: scale(1.15);
}

.input {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  background: rgba(255, 255, 255, 0.35);
  color: var(--text-primary);
  box-sizing: border-box;
  font-size: 0.9rem;
}
</style>
