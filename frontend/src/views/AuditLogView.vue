<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { auditLogApi, type AuditLogEntry } from '../api/auditLog'

const logs = ref<AuditLogEntry[]>([])
const total = ref(0)
const loading = ref(true)
const page = ref(1)
const pageSize = 20
const filterResource = ref('')
const selectedLog = ref<AuditLogEntry | null>(null)

interface TechnicalDetail {
  method?: string
  path?: string
  statusCode?: number
  ip?: string
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
      <select v-model="filterResource" class="filter-select">
        <option value="">全部资源</option>
        <option value="交易员">交易员</option>
        <option value="交易所">交易所</option>
        <option value="策略">策略</option>
        <option value="系统设置">系统设置</option>
        <option value="用户">用户</option>
        <option value="认证">认证</option>
        <option value="订单">订单</option>
        <option value="通知渠道">通知渠道</option>
      </select>
      <button class="btn-primary" @click="load">筛选</button>
    </div>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th>时间</th>
          <th>操作</th>
          <th>资源</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="log in logs" :key="log.id" class="log-row" @click="openDetail(log)">
          <td class="time-cell">{{ new Date(log.timestamp).toLocaleString() }}</td>
          <td><span class="badge">{{ log.action }}</span></td>
          <td>
            <span class="resource-text">{{ log.resource }}</span>
            <span v-if="log.resourceId" class="resource-id">#{{ log.resourceId.slice(0, 8) }}</span>
          </td>
        </tr>
        <tr v-if="logs.length === 0">
          <td colspan="3" class="empty">暂无审计日志</td>
        </tr>
      </tbody>
    </table>

    <div v-if="total > pageSize" class="pagination">
      <button :disabled="page <= 1" @click="page--; load()">上一页</button>
      <span>{{ page }} / {{ Math.ceil(total / pageSize) }}</span>
      <button :disabled="page * pageSize >= total" @click="page++; load()">下一页</button>
    </div>

    <div v-if="selectedLog" class="modal-overlay" @click.self="closeDetail">
      <div class="modal">
        <h3>操作详情</h3>

        <div class="summary-row">
          <span>{{ new Date(selectedLog.timestamp).toLocaleString() }}</span>
          <span class="badge">{{ selectedLog.action }}</span>
          <span class="resource-text">{{ selectedLog.resource }}</span>
          <span v-if="selectedLog.resourceId" class="resource-id">#{{ selectedLog.resourceId.slice(0, 8) }}</span>
        </div>

        <div class="tech-grid">
          <div class="tech-item">
            <span class="tech-label">HTTP 方法</span>
            <span class="tech-value"><code>{{ parseDetail(selectedLog).method || '-' }}</code></span>
          </div>
          <div class="tech-item">
            <span class="tech-label">完整路径</span>
            <span class="tech-value"><code>{{ parseDetail(selectedLog).path || '-' }}</code></span>
          </div>
          <div class="tech-item">
            <span class="tech-label">状态码</span>
            <span class="tech-value"><code>{{ parseDetail(selectedLog).statusCode ?? '-' }}</code></span>
          </div>
          <div class="tech-item">
            <span class="tech-label">IP 地址</span>
            <span class="tech-value"><code>{{ selectedLog.ipAddress }}</code></span>
          </div>
          <div class="tech-item">
            <span class="tech-label">用户 ID</span>
            <span class="tech-value"><code>{{ selectedLog.userId ? selectedLog.userId.slice(0, 8) + '...' : '-' }}</code></span>
          </div>
        </div>

        <details class="raw-toggle">
          <summary>查看原始 JSON</summary>
          <pre class="raw-json">{{ JSON.stringify(parseDetail(selectedLog), null, 2) }}</pre>
        </details>

        <div class="modal-actions">
          <button class="btn-primary" @click="closeDetail">关闭</button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.audit-page { padding: 2rem; }
h2 { margin: 0 0 1rem; color: #e2e8f0; }
.filters { display: flex; gap: 0.75rem; margin-bottom: 1rem; flex-wrap: wrap; }
.filter-select { padding: 0.5rem; background: #0f172a; color: #e2e8f0; border: 1px solid #334155; border-radius: 4px; min-width: 180px; }
.btn-primary { padding: 0.5rem 1rem; background: #38bdf8; color: #0f172a; border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.625rem 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; font-size: 0.85rem; }
.table th { color: #94a3b8; font-weight: 600; }
.log-row { cursor: pointer; transition: background 0.1s; }
.log-row:hover { background: rgba(56, 189, 248, 0.04); }
.time-cell { color: #64748b; white-space: nowrap; }
.badge { display: inline-block; padding: 0.15rem 0.5rem; background: rgba(56,189,248,0.1); color: #38bdf8; border-radius: 4px; font-size: 0.8rem; }
.resource-text { color: #94a3b8; }
.resource-id { color: #38bdf8; font-family: monospace; font-size: 0.8rem; margin-left: 0.375rem; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.pagination { display: flex; justify-content: center; align-items: center; gap: 1rem; margin-top: 1rem; }
.pagination button { padding: 0.4rem 0.8rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; }
.pagination button:disabled { opacity: 0.5; cursor: not-allowed; }
.pagination span { color: #94a3b8; font-size: 0.85rem; }
.modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; justify-content: center; align-items: center; z-index: 100; }
.modal { background: #1e293b; padding: 2rem; border-radius: 8px; width: 100%; max-width: 520px; }
.modal h3 { margin: 0 0 1rem; color: #e2e8f0; }
.summary-row { display: flex; align-items: center; gap: 0.75rem; flex-wrap: wrap; margin-bottom: 1.25rem; padding: 0.5rem 0.75rem; background: #0f172a; border-radius: 6px; font-size: 0.85rem; }
.tech-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 1rem; margin-bottom: 1rem; }
.tech-item { display: flex; flex-direction: column; gap: 0.25rem; }
.tech-label { color: #64748b; font-size: 0.75rem; }
.tech-value { color: #e2e8f0; font-size: 0.85rem; }
.tech-value code { color: #38bdf8; background: rgba(56,189,248,0.08); padding: 0.125rem 0.375rem; border-radius: 3px; font-size: 0.8rem; }
.raw-toggle { color: #64748b; font-size: 0.8rem; margin-bottom: 1rem; }
.raw-toggle summary { cursor: pointer; }
.raw-json { background: #0f172a; border: 1px solid #334155; border-radius: 4px; padding: 0.75rem; font-family: monospace; font-size: 0.8rem; color: #94a3b8; overflow-x: auto; margin-top: 0.5rem; }
.modal-actions { display: flex; justify-content: flex-end; }
</style>
