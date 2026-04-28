<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { auditLogApi, type AuditLogEntry } from '../api/auditLog'

const logs = ref<AuditLogEntry[]>([])
const total = ref(0)
const loading = ref(true)
const page = ref(1)
const pageSize = ref(15)
const filterResource = ref('')
const selectedLog = ref<AuditLogEntry | null>(null)

const columns = [
  { title: '#', dataIndex: 'index', width: 60 },
  { title: '时间', dataIndex: 'timestamp', width: 200 },
  { title: '操作', dataIndex: 'action', width: 160 },
  { title: '资源', dataIndex: 'resource', ellipsis: true }
]

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

function formatTime(value: string): string {
  const d = new Date(value)
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}`
}

async function load() {
  loading.value = true
  try {
    const { data } = await auditLogApi.getAll({
      page: page.value, pageSize: pageSize.value,
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
    <a-card class="filter-card">
      <a-select
        :model-value="filterResource"
        style="width: 160px"
        @change="(v) => filterResource = String(v)"
      >
        <a-option value="" label="全部资源" />
        <a-option value="交易员" label="交易员" />
        <a-option value="交易所" label="交易所" />
        <a-option value="策略" label="策略" />
        <a-option value="系统设置" label="系统设置" />
        <a-option value="用户" label="用户" />
        <a-option value="认证" label="认证" />
        <a-option value="订单" label="订单" />
        <a-option value="通知渠道" label="通知渠道" />
      </a-select>
      <a-button type="primary" @click="load">
        <template #icon><icon-filter /></template>
        筛选
      </a-button>
    </a-card>

    <a-table
      :columns="columns"
      :data="logs"
      :loading="loading"
      :pagination="{
        current: page,
        pageSize: pageSize,
        total,
        simple: true,
        showTotal: true,
        showPageSize: true,
        pageSizeOptions: [10, 15, 20, 50, 100]
      }"
      page-position="top"
      :row-class="() => 'log-row'"
      @row-click="(record: any) => openDetail(record as AuditLogEntry)"
      @page-change="(p: number) => { page = p; load() }"
      @page-size-change="(s: number) => { pageSize = s; page = 1; load() }"
      stripe
    >
      <template #cell-index="{ rowIndex }">{{ (page - 1) * pageSize + rowIndex + 1 }}</template>
      <template #cell-timestamp="{ record }">
        <span class="time-cell">{{ record.timestamp }}</span>
      </template>
      <template #cell-action="{ record }">
        <a-tag>{{ formatAction(record) }}</a-tag>
      </template>
      <template #cell-resource="{ record }">
        <span class="resource-text">{{ record.resource }}</span>
        <span v-if="record.resourceId" class="resource-id">#{{ record.resourceId.slice(0, 8) }}</span>
      </template>
    </a-table>

    <a-modal :visible="!!selectedLog" title="操作详情" width="md" :mask-closable="false" @cancel="closeDetail">
      <template v-if="selectedLog">
        <div class="detail-grid">
          <div class="detail-field">
            <span class="detail-label">时间</span>
            <span class="detail-value"><code>{{ formatTime(selectedLog.timestamp) }}</code></span>
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
    </a-modal>
  </div>
</template>

<style scoped>
.audit-page { padding: 0; }
.filter-card { margin-bottom: 1rem; }
.filter-card :deep(.arco-card-body) {
  padding: 0.75rem 1rem;
  display: flex;
  gap: 0.75rem;
}
.log-row { cursor: pointer; }
.log-row:hover { background: rgba(79, 126, 201, 0.04); }
.time-cell { color: var(--text-muted); white-space: nowrap; }
.resource-text { color: var(--text-muted); }
.resource-id { color: var(--accent-blue); font-family: monospace; font-size: 0.8rem; margin-left: 0.375rem; }
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
