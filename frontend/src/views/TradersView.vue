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

const tagStatusColors: Record<string, string> = {
  Active: 'green', Disabled: '', Deleted: 'red'
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
  const str = String(status ?? '')
  const lower = str.toLowerCase()
  if (lower === 'active') return 'Active'
  if (lower === 'disabled') return 'Disabled'
  if (lower === 'deleted') return 'Deleted'
  return str
}

function getStatusLabel(status: unknown): string {
  const normalized = normalizeStatus(status)
  return statusLabels[normalized] ?? (normalized || '-')
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
      <a-button type="primary" @click="openCreate">
        <template #icon><icon-plus /></template>
        新建交易员
      </a-button>
    </header>

    <a-modal v-model:visible="showForm" :title="editId ? '编辑交易员' : '新建交易员'" width="sm" :mask-closable="false">
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
            <icon-edit :size="14" />
          </div>
        </div>
        <div v-else class="form-avatar-img-wrap" @click="avatarInput?.click()" :title="editId ? '点击更换头像' : ''">
          <img :src="formAvatarUrl" alt="" class="form-avatar-img" />
          <div class="avatar-overlay">
            <icon-edit :size="14" />
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
        <a-input v-model="formName" placeholder="输入交易员名称" @keyup.enter="save" />
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
        <a-button type="primary" :disabled="uploading" @click="save">
          <template #icon><icon-save /></template>
          保存
        </a-button>
      </template>
    </a-modal>

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
              <a-tag :color="tagStatusColors[normalizeStatus(t.status)] || ''">{{ getStatusLabel(t.status) }}</a-tag>
            </span>
          </div>
          <div class="card-header-actions">
            <a-button size="mini" type="text" title="编辑名称" @click="openEdit(t)">
              <template #icon><icon-edit /></template>
            </a-button>
            <a-switch :model-value="normalizeStatus(t.status) === 'Active'" @change="() => toggleStatus(t)" />
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
          <a-button size="small" @click="router.push(`/traders/${t.id}/strategies`)">
            <template #icon><icon-common /></template>
            策略部署
          </a-button>
          <a-button size="small" @click="router.push(`/traders/${t.id}/positions`)">
            <template #icon><icon-common /></template>
            持仓
          </a-button>
          <a-button size="small" @click="router.push(`/traders/${t.id}/orders`)">
            <template #icon><icon-list /></template>
            订单
          </a-button>
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
.trader-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); gap: 1rem; }
.trader-card { background: var(--card-bg, #fff); border: 1px solid var(--glass-border); border-top: 3px solid; border-radius: 8px; overflow: hidden; transition: box-shadow 0.2s ease, transform 0.2s ease; }
.trader-card:hover { box-shadow: 0 8px 28px rgba(0, 0, 0, 0.06); transform: translateY(-2px); }
.card-header { display: flex; align-items: center; gap: 0.75rem; padding: 1rem 1.25rem 0.5rem; }
.trader-avatar { width: 40px; height: 40px; border-radius: 50%; display: flex; align-items: center; justify-content: center; font-weight: 700; font-size: 1rem; flex-shrink: 0; }
.trader-avatar-img { width: 40px; height: 40px; border-radius: 50%; object-fit: cover; flex-shrink: 0; }
.trader-meta { flex: 1; min-width: 0; }
.trader-name { display: block; color: var(--text-primary); font-size: 0.95rem; }
.trader-status { display: flex; align-items: center; gap: 0.3rem; font-size: 0.78rem; margin-top: 0.1rem; }
.card-header-actions { display: flex; align-items: center; gap: 0.5rem; align-self: flex-start; }
.card-tag-row { padding: 0 1.25rem 0.5rem; }
.style-tag { display: inline-block; padding: 0.15rem 0.5rem; border-radius: 4px; font-size: 0.75rem; font-weight: 600; border: 1px solid; }
.card-stats { display: grid; grid-template-columns: repeat(2, 1fr); gap: 0.5rem; padding: 0.5rem 1.25rem 0.75rem; }
.stat-item { display: flex; flex-direction: column; gap: 0.1rem; }
.stat-num { font-size: 1.25rem; font-weight: 700; letter-spacing: -0.03em; color: var(--text-primary); }
.stat-label { font-size: 0.72rem; color: var(--text-muted); }
.num-up { color: var(--accent-green); }
.num-down { color: var(--accent-red); }
.num-neutral { color: var(--accent-amber); }
.card-actions { display: flex; gap: 0.5rem; padding: 0.75rem 1.25rem 1rem; border-top: 1px solid var(--glass-border); }
.card-actions :deep(.arco-btn) { flex: 1; }
.form-preview { display: flex; align-items: center; gap: 1rem; margin-bottom: 1rem; }
.form-avatar-clickable, .form-avatar-img-wrap { width: 60px; height: 60px; border-radius: 50%; display: flex; align-items: center; justify-content: center; cursor: pointer; position: relative; overflow: hidden; flex-shrink: 0; }
.form-avatar-img { width: 100%; height: 100%; object-fit: cover; }
.avatar-letter { font-size: 1.3rem; font-weight: 700; }
.avatar-overlay { position: absolute; inset: 0; background: rgba(0,0,0,0.35); display: flex; align-items: center; justify-content: center; opacity: 0; transition: opacity 0.15s; border-radius: 50%; color: #fff; }
.form-avatar-clickable:hover .avatar-overlay, .form-avatar-img-wrap:hover .avatar-overlay { opacity: 1; }
.form-meta { flex: 1; }
.form-meta strong { display: block; color: var(--text-primary); font-size: 0.95rem; }
.form-meta span { color: var(--text-muted); font-size: 0.8rem; }
.form-field { margin-bottom: 0.75rem; }
.form-label { display: block; color: var(--text-muted); font-size: 0.85rem; margin-bottom: 0.25rem; font-weight: 500; }
.form-error { color: var(--accent-red); font-size: 0.85rem; margin-bottom: 0.5rem; }
.hidden-input { display: none; }
.style-options { display: flex; flex-wrap: wrap; gap: 0.375rem; }
.style-chip { padding: 0.3rem 0.6rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-muted); cursor: pointer; font-size: 0.78rem; transition: all 0.12s; }
.style-chip.active { border-color: var(--chip-border); background: var(--chip-bg); color: var(--chip-color); font-weight: 600; }
.style-clear { color: var(--accent-red); border-color: rgba(191, 72, 72, 0.28); }
</style>
