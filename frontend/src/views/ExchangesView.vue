<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { exchangesApi, type ExchangeAccount as Exchange } from '../api/exchanges'
import ExchangeTypeSelect from '../components/ExchangeTypeSelect.vue'

const accounts = ref<Exchange[]>([])
const loading = ref(true)
const balances = ref<Record<string, number>>({})
const balanceLoading = ref(false)
const showForm = ref(false)
const editId = ref<string | null>(null)
const testingId = ref<string | null>(null)
const testResult = ref<{ id: string; connected: boolean; error?: string } | null>(null)
const toast = ref({ message: '', type: '' as 'success' | 'error' })

function showToast(message: string, type: 'success' | 'error') {
  toast.value = { message, type }
  setTimeout(() => { toast.value.message = '' }, 4000)
}

const formLabel = ref('')
const formExchangeType = ref('Binance')
const formApiKey = ref('')
const formSecretKey = ref('')
const formPassphrase = ref('')
const formIsTestnet = ref(false)

async function loadAll() {
  loading.value = true
  try {
    const { data } = await exchangesApi.getAll()
    accounts.value = data.data ?? []
  } finally {
    loading.value = false
  }
  await fetchBalances()
}

async function fetchBalances() {
  const enabled = accounts.value.filter(a => a.isEnabled)
  if (!enabled.length) return
  balanceLoading.value = true
  try {
    const results = await Promise.allSettled(
      enabled.map(a => exchangesApi.getBalance(a.id).then(r => ({ id: a.id, totalUsd: r.data.totalUsd })))
    )
    for (const r of results) {
      if (r.status === 'fulfilled') {
        balances.value[r.value.id] = r.value.totalUsd
      }
    }
  } finally {
    balanceLoading.value = false
  }
}

function openCreate() {
  editId.value = null
  formLabel.value = ''
  formExchangeType.value = 'Binance'
  formApiKey.value = ''
  formSecretKey.value = ''
  formPassphrase.value = ''
  formIsTestnet.value = false
  showForm.value = true
}

function openEdit(account: Exchange) {
  editId.value = account.id
  formLabel.value = account.label
  formExchangeType.value = account.exchangeType
  formApiKey.value = ''
  formSecretKey.value = ''
  formPassphrase.value = ''
  formIsTestnet.value = account.isTestnet
  showForm.value = true
}

async function save() {
  if (editId.value) {
    const payload: Record<string, unknown> = { name: formLabel.value }
    if (formApiKey.value) payload.apiKey = formApiKey.value
    if (formSecretKey.value) payload.secretKey = formSecretKey.value
    if (formPassphrase.value) payload.passphrase = formPassphrase.value
    await exchangesApi.update(editId.value, payload)
  } else {
    await exchangesApi.create({
      name: formLabel.value,
      exchangeType: formExchangeType.value,
      apiKey: formApiKey.value,
      secretKey: formSecretKey.value,
      passphrase: formPassphrase.value || undefined,
      isTestnet: formIsTestnet.value
    })
  }
  showForm.value = false
  await loadAll()
}

async function remove(id: string) {
  await exchangesApi.delete(id)
  await loadAll()
}

async function testConnection(id: string) {
  testingId.value = id
  testResult.value = null
  try {
    const { data } = await exchangesApi.testConnection(id)
    testResult.value = { id, ...data }
    if (data.connected) {
      showToast('连接测试成功', 'success')
    } else {
      showToast(`连接失败${data.error ? ': ' + data.error : ''}`, 'error')
    }
    await loadAll()
  } catch {
    testResult.value = { id, connected: false, error: '请求失败' }
    showToast('请求失败', 'error')
  } finally {
    testingId.value = null
    setTimeout(() => { testResult.value = null }, 3000)
  }
}

onMounted(loadAll)
</script>

<template>
  <div class="exchanges-page">
    <header class="page-header">
      <h2>交易所管理</h2>
      <AppButton variant="primary" icon="plus" @click="openCreate">添加交易所</AppButton>
    </header>

    <Transition name="toast-fade">
      <div v-if="toast.message" class="toast" :class="`toast--${toast.type}`">
        {{ toast.message }}
      </div>
    </Transition>

    <AppModal v-model="showForm" :title="editId ? '编辑交易所' : '添加交易所'" width="sm">
      <input v-model="formLabel" placeholder="名称（如：币安主账户）" class="input" />
      <ExchangeTypeSelect v-model="formExchangeType" :disabled="!!editId" />
      <input v-model="formApiKey" :placeholder="editId ? '留空则不修改' : 'API Key'" type="password" class="input" />
      <input v-model="formSecretKey" :placeholder="editId ? '留空则不修改' : 'Secret Key'" type="password" class="input" />
      <input v-model="formPassphrase" placeholder="Passphrase（选填）" type="password" class="input" />
      <label class="checkbox-label">
        <input v-model="formIsTestnet" type="checkbox" />
        测试网
      </label>
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
          <th>交易所</th>
          <th>模式</th>
          <th>状态</th>
          <th>最近测试</th>
          <th>测试结果</th>
          <th>资产总额</th>
          <th>创建时间</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="a in accounts" :key="a.id">
          <td>{{ a.label }}</td>
          <td>{{ a.exchangeType }}</td>
          <td>{{ a.isTestnet ? '测试网' : '主网' }}</td>
          <td>{{ a.isEnabled ? '启用' : '禁用' }}</td>
          <td>{{ a.lastTestedAt ? new Date(a.lastTestedAt).toLocaleString('zh-CN', { hour12: false }) : '-' }}</td>
          <td>
            <span :class="['test-badge', a.testResult && !a.testResult.includes('失败') && !a.testResult.includes('error') && !a.testResult.includes('invalid') ? 'test-badge--ok' : 'test-badge--fail']" :title="a.testResult || ''">
              <span class="test-dot" />
              {{ a.testResult ? (a.testResult.length > 20 ? a.testResult.slice(0, 20) + '…' : a.testResult) : '-' }}
            </span>
          </td>
          <td class="balance-cell">
            <template v-if="a.isEnabled">
              <span v-if="balances[a.id] != null" class="balance-value">${{ balances[a.id].toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) }}</span>
              <span v-else-if="balanceLoading" class="balance-loading">计算中...</span>
              <AppButton v-else size="sm" variant="ghost" @click="fetchBalances">加载</AppButton>
            </template>
            <span v-else class="balance-muted">-</span>
          </td>
          <td>{{ new Date(a.createdAt).toLocaleDateString() }}</td>
          <td class="actions">
            <AppButton size="sm" icon="test" :disabled="testingId === a.id" @click="testConnection(a.id)">{{ testingId === a.id ? '测试中...' : '测试连接' }}</AppButton>
            <AppButton size="sm" icon="edit" @click="openEdit(a)">编辑</AppButton>
            <AppButton size="sm" variant="danger" icon="trash" @click="remove(a.id)">删除</AppButton>
          </td>
        </tr>
        <tr v-if="accounts.length === 0">
          <td colspan="7" class="empty">暂无交易所</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.exchanges-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.page-header h2 { margin: 0; color: var(--text-primary); }
.btn-primary { padding: 0.5rem 1rem; background: var(--accent-blue); color: var(--text-primary); border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: var(--text-primary); border: 1px solid var(--glass-border-strong); border-radius: 4px; cursor: pointer; }
.btn-small { padding: 0.25rem 0.75rem; background: #334155; color: var(--text-primary); border: 1px solid var(--glass-border-strong); border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
.btn-small:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-danger { color: var(--accent-red); border-color: var(--accent-red); background: transparent; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); }
.table th { color: var(--text-muted); font-weight: 600; }
.actions { display: flex; gap: 0.5rem; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.toast {
  position: fixed;
  top: 1rem;
  left: 50%;
  transform: translateX(-50%);
  z-index: 1200;
  padding: 0.75rem 1.5rem;
  border-radius: 999px;
  font-size: 0.85rem;
  backdrop-filter: blur(24px) saturate(180%);
  -webkit-backdrop-filter: blur(24px) saturate(180%);
  box-shadow: 0 18px 50px rgba(2, 6, 23, 0.28);
  pointer-events: none;
  border-radius: 6px;
}
.toast--success {
  background: rgba(34, 197, 94, 0.18);
  border: 1px solid rgba(34, 197, 94, 0.38);
  color: #bbf7d0;
}
.toast--error {
  background: rgba(239, 68, 68, 0.18);
  border: 1px solid rgba(239, 68, 68, 0.38);
  color: #fecaca;
}
.toast-fade-enter-active, .toast-fade-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}
.toast-fade-enter-from, .toast-fade-leave-to {
  opacity: 0;
  transform: translateY(-0.5rem);
}
.test-badge {
  display: inline-flex;
  align-items: center;
  gap: 0.3rem;
  padding: 0.15rem 0.5rem;
  border-radius: 999px;
  font-size: 0.78rem;
  max-width: 180px;
}
.test-badge--ok {
  background: rgba(34, 197, 94, 0.10);
  border: 1px solid rgba(34, 197, 94, 0.26);
  color: #86efac;
}
.test-badge--fail {
  background: rgba(239, 68, 68, 0.10);
  border: 1px solid rgba(239, 68, 68, 0.26);
  color: #fca5a5;
}
.test-dot {
  width: 0.45rem;
  height: 0.45rem;
  border-radius: 50%;
  flex-shrink: 0;
  background: currentColor;
}
.input { width: 100%; padding: 0.75rem; margin-bottom: 1rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-primary); box-sizing: border-box; }
.input:is(select) { cursor: pointer; }
.balance-cell { text-align: right; white-space: nowrap; }
.balance-value { color: var(--accent-green); font-weight: 600; }
.balance-loading { color: var(--text-muted); font-size: 0.8rem; }
.balance-muted { color: var(--text-muted); }
.checkbox-label { display: flex; align-items: center; gap: 0.5rem; color: var(--text-primary); margin-bottom: 1rem; cursor: pointer; }
.checkbox-label input { width: auto; margin: 0; }
</style>
