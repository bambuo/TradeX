<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { auditLogApi, type AuditLogEntry } from '../api/auditLog'

const logs = ref<AuditLogEntry[]>([])
const total = ref(0)
const loading = ref(true)
const page = ref(1)
const pageSize = 20
const filterAction = ref('')
const filterResourceType = ref('')

const actionLabels: Record<string, string> = {
  'user.login': '用户登录', 'user.logout': '用户登出',
  'user.created': '创建用户', 'user.role_changed': '角色变更',
  'exchange.created': '创建交易所', 'exchange.updated': '更新交易所',
  'exchange.deleted': '删除交易所', 'exchange.test': '测试连接',
  'strategy.created': '创建策略', 'strategy.updated': '更新策略',
  'strategy.deleted': '删除策略', 'strategy.enabled': '启用策略',
  'strategy.disabled': '停用策略', 'order.manual': '手动下单',
  'settings.updated': '更新设置'
}

async function load() {
  loading.value = true
  try {
    const { data } = await auditLogApi.getAll({
      page: page.value, pageSize,
      action: filterAction.value || undefined,
      resourceType: filterResourceType.value || undefined
    })
    logs.value = data.data
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
      <select v-model="filterAction" class="filter-select">
        <option value="">全部操作</option>
        <option v-for="(label, key) in actionLabels" :key="key" :value="key">{{ label }}</option>
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
          <th>详情</th>
          <th>IP</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="log in logs" :key="log.id">
          <td>{{ new Date(log.timestamp).toLocaleString() }}</td>
          <td><span class="badge">{{ actionLabels[log.action] || log.action }}</span></td>
          <td>{{ log.resource }} {{ log.resourceId ? `(${log.resourceId.slice(0, 8)}...)` : '' }}</td>
          <td class="detail-cell">{{ log.detail || '-' }}</td>
          <td>{{ log.ipAddress }}</td>
        </tr>
        <tr v-if="logs.length === 0">
          <td colspan="5" class="empty">暂无审计日志</td>
        </tr>
      </tbody>
    </table>

    <div v-if="total > pageSize" class="pagination">
      <button :disabled="page <= 1" @click="page--; load()">上一页</button>
      <span>{{ page }} / {{ Math.ceil(total / pageSize) }}</span>
      <button :disabled="page * pageSize >= total" @click="page++; load()">下一页</button>
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
.table th, .table td { padding: 0.6rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; font-size: 0.85rem; }
.table th { color: #94a3b8; font-weight: 600; }
.badge { padding: 0.15rem 0.5rem; background: rgba(56,189,248,0.1); color: #38bdf8; border-radius: 4px; font-size: 0.8rem; }
.detail-cell { max-width: 250px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.pagination { display: flex; justify-content: center; align-items: center; gap: 1rem; margin-top: 1rem; }
.pagination button { padding: 0.4rem 0.8rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; }
.pagination button:disabled { opacity: 0.5; cursor: not-allowed; }
.pagination span { color: #94a3b8; font-size: 0.85rem; }
</style>
