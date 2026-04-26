<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { auditLogApi, type AuditLogEntry } from '../api/auditLog'
import AppSelect from '../components/AppSelect.vue'

const logs = ref<AuditLogEntry[]>([])
const total = ref(0)
const loading = ref(true)
const page = ref(1)
const pageSize = 15
const filterResource = ref('')
const selectedLog = ref<AuditLogEntry | null>(null)

interface TechnicalDetail {
  method?: string
  path?: string
  statusCode?: number
  ip?: string
}

const resourceLabels: Record<string, string> = {
  traders: '交易员',
  exchanges: '交易所',
  strategies: '策略',
  settings: '系统设置',
  users: '用户',
  auth: '认证',
  orders: '订单',
  positions: '持仓',
  notifications: '通知渠道'
}

const subResourceLabels: Record<string, string> = {
  strategies: '策略部署',
  exchanges: '交易所',
  orders: '订单',
  positions: '持仓',
  channels: '通知渠道'
}

const verbLabels: Record<string, string> = {
  POST: '创建',
  PUT: '更新',
  DELETE: '删除',
  PATCH: '修改'
}

function parseDetail(log: AuditLogEntry): TechnicalDetail {
  try { return JSON.parse(log.detail ?? '{}') }
  catch { return {} }
}

function openDetail(log: AuditLogEntry) {
  selectedLog.value = log
}

function closeDetail() {
  selectedLog.value = null
}

function handleDetailOpenChange(open: boolean) {
  if (!open) closeDetail()
}

function formatAction(log: AuditLogEntry): string {
  const detail = parseDetail(log)
  if (detail.path && detail.method) {
    const parsed = actionFromPath(detail.method, detail.path)
    if (parsed) return parsed
  }

  return translateActionText(log.action)
}

function actionFromPath(method: string, path: string): string | null {
  const segments = path.split('?')[0].split('/').filter(Boolean)
  if (segments[0] !== 'api' || segments.length < 2) return null

  const resource = segments[1]
  const resourceName = resourceLabels[resource] ?? resource
  const id = segments[2]
  const sub = segments[3]
  const subId = segments[4]
  const action = segments[5]

  if (resource === 'auth') return authActionLabel(segments.slice(2).join('/'))
  if (sub === 'test') return '测试连接'
  if (sub === 'backtests') return '执行回测'
  if (sub === 'manual') return '手动下单'
  if (action === 'toggle') return `启用/禁用${subResourceLabels[sub] ?? resourceName}`
  if (sub === 'toggle') return `启用/禁用${resourceName}`

  const verb = verbLabels[method] ?? method
  if (!verbLabels[method]) return translateActionText(`${method} ${path}`)

  if (sub) {
    const target = subResourceLabels[sub] ?? sub
    if (method === 'POST' && !subId) return `新建${target}`
    if (method === 'DELETE') return `删除${target}`
    if (method === 'PUT') return `更新${target}`
    return `${verb}${target}`
  }

  if (method === 'POST' && !id) return `新建${resourceName}`
  if (method === 'DELETE') return `删除${resourceName}`
  if (method === 'PUT') return `更新${resourceName}`

  return `${verb}${resourceName}`
}

function authActionLabel(path: string): string {
  const labels: Record<string, string> = {
    login: '登录',
    logout: '登出',
    refresh: '刷新令牌',
    'mfa/setup': '设置 MFA',
    'mfa/verify': '验证 MFA',
    'send-recovery-codes': '发送恢复码'
  }
  return labels[path] ?? translateActionText(path)
}

function translateActionText(action: string): string {
  return action
    .replace(/POST/g, '创建')
    .replace(/PUT/g, '更新')
    .replace(/DELETE/g, '删除')
    .replace(/PATCH/g, '修改')
    .replace(/GET/g, '查询')
    .replace(/traders/g, '交易员')
    .replace(/exchanges/g, '交易所')
    .replace(/strategies/g, '策略')
    .replace(/settings/g, '系统设置')
    .replace(/users/g, '用户')
    .replace(/auth/g, '认证')
    .replace(/refresh/g, '刷新令牌')
    .replace(/login/g, '登录')
    .replace(/logout/g, '登出')
    .replace(/orders/g, '订单')
    .replace(/positions/g, '持仓')
    .replace(/notifications/g, '通知渠道')
    .replace(/backtests/g, '回测')
    .replace(/toggle/g, '启用/禁用')
    .replace(/test/g, '测试连接')
    .replace(/manual/g, '手动下单')
}

async function load() {
  loading.value = true
  try {
    const { data } = await auditLogApi.getAll({
      page: page.value, pageSize,
      resourceType: filterResource.value || undefined
    })
    logs.value = data.data ?? []
    total.value = data.total
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="audit-page">
    <h2>审计日志</h2>

    <div class="filters">
      <AppSelect
        form
        :options="[
          { label: '全部资源', value: '' },
          { label: '交易员', value: '交易员' },
          { label: '交易所', value: '交易所' },
          { label: '策略', value: '策略' },
          { label: '系统设置', value: '系统设置' },
          { label: '用户', value: '用户' },
          { label: '认证', value: '认证' },
          { label: '订单', value: '订单' },
          { label: '通知渠道', value: '通知渠道' },
        ]"
        :model-value="filterResource"
        @update:model-value="(v: string | number) => filterResource = String(v)"
      />
      <AppButton variant="primary" icon="filter" @click="load">筛选</AppButton>
    </div>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th class="row-num">#</th>
          <th>时间</th>
          <th>操作</th>
          <th>资源</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(log, index) in logs" :key="log.id" class="log-row" @click="openDetail(log)">
          <td class="row-num">{{ (page - 1) * pageSize + index + 1 }}</td>
          <td class="time-cell">{{ new Date(log.timestamp).toLocaleString() }}</td>
          <td><span class="badge">{{ formatAction(log) }}</span></td>
          <td>
            <span class="resource-text">{{ log.resource }}</span>
            <span v-if="log.resourceId" class="resource-id">#{{ log.resourceId.slice(0, 8) }}</span>
          </td>
        </tr>
        <tr v-if="logs.length === 0">
          <td colspan="4" class="empty">暂无审计日志</td>
        </tr>
      </tbody>
    </table>

    <div v-if="total > pageSize" class="pagination">
      <AppButton size="sm" icon="back" :disabled="page <= 1" @click="page--; load()">上一页</AppButton>
      <span class="page-info">{{ page }} / {{ Math.ceil(total / pageSize) }}</span>
      <AppButton size="sm" :disabled="page * pageSize >= total" @click="page++; load()">下一页</AppButton>
    </div>

    <AppModal :model-value="!!selectedLog" title="操作详情" width="md" @update:model-value="handleDetailOpenChange" @close="closeDetail">
      <template v-if="selectedLog">
        <div class="detail-grid">
          <div class="detail-field">
            <span class="detail-label">时间</span>
            <span class="detail-value"><code>{{ new Date(selectedLog.timestamp).toLocaleString() }}</code></span>
          </div>
          <div class="detail-field">
            <span class="detail-label">资源</span>
            <span class="detail-value">
              <code>{{ selectedLog.resource }}</code>
              <span v-if="selectedLog.resourceId" class="resource-id">#{{ selectedLog.resourceId.slice(0, 8) }}</span>
            </span>
          </div>
          <div class="detail-field">
            <span class="detail-label">HTTP 方法</span>
            <span class="detail-value"><code>{{ parseDetail(selectedLog).method || '-' }}</code></span>
          </div>
          <div class="detail-field">
            <span class="detail-label">状态码</span>
            <span class="detail-value"><code>{{ parseDetail(selectedLog).statusCode ?? '-' }}</code></span>
          </div>
          <div class="detail-field detail-field--full">
            <span class="detail-label">完整路径</span>
            <span class="detail-value detail-value--path"><code>{{ parseDetail(selectedLog).path || '-' }}</code></span>
          </div>
          <div class="detail-field">
            <span class="detail-label">IP 地址</span>
            <span class="detail-value"><code>{{ selectedLog.ipAddress }}</code></span>
          </div>
          <div class="detail-field">
            <span class="detail-label">用户 ID</span>
            <span class="detail-value"><code>{{ selectedLog.userId ? selectedLog.userId.slice(0, 8) + '...' : '-' }}</code></span>
          </div>
        </div>

        <details v-if="selectedLog" class="raw-toggle">
          <summary>查看原始 JSON</summary>
          <pre class="raw-json">{{ JSON.stringify(parseDetail(selectedLog), null, 2) }}</pre>
        </details>
      </template>
    </AppModal>
  </div>
</template>

<style scoped>
.audit-page { padding: 2rem; }
h2 { margin: 0 0 1rem; color: var(--text-primary); }
.filters { display: flex; gap: 0.75rem; margin-bottom: 1rem; flex-wrap: wrap; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.625rem 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); font-size: 0.85rem; }
.table th { color: var(--text-muted); font-weight: 600; }
.log-row { cursor: pointer; transition: background 0.1s; }
.log-row:hover { background: rgba(79, 126, 201, 0.04); }
.time-cell { color: var(--text-muted); white-space: nowrap; }
.row-num { width: 2.5rem; text-align: center; color: var(--text-muted); font-size: 0.8rem; }
.badge { display: inline-block; padding: 0.15rem 0.5rem; background: rgba(56,189,248,0.1); color: var(--accent-blue); border-radius: 4px; font-size: 0.8rem; }
.resource-text { color: var(--text-muted); }
.resource-id { color: var(--accent-blue); font-family: monospace; font-size: 0.8rem; margin-left: 0.375rem; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.pagination { display: flex; justify-content: center; align-items: center; gap: 0.75rem; margin-top: 1rem; }
.page-info { color: var(--text-muted); font-size: 0.85rem; }
.detail-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0.75rem 1rem; margin-bottom: 1rem; }
.detail-field { display: flex; flex-direction: column; gap: 0.2rem; min-width: 0; }
.detail-field--full { grid-column: 1 / -1; }
.detail-label { color: var(--text-muted); font-size: 0.75rem; }
.detail-value { color: var(--text-primary); font-size: 0.85rem; }
.detail-value code { color: var(--accent-blue); background: rgba(56,189,248,0.08); padding: 0.125rem 0.375rem; border-radius: 3px; font-size: 0.8rem; }
.detail-value--path { overflow: hidden; }
.detail-value--path code { display: inline-block; max-width: 100%; overflow-x: auto; white-space: nowrap; }
.raw-toggle { color: var(--text-muted); font-size: 0.8rem; margin-top: 0.5rem; }
.raw-toggle summary { cursor: pointer; }
.raw-json { background: rgba(255,255,255,0.35); border: 1px solid var(--glass-border); border-radius: 4px; padding: 0.75rem; font-family: monospace; font-size: 0.8rem; color: var(--text-muted); overflow-x: auto; margin-top: 0.5rem; }
</style>
