<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { exchangesApi, type ExchangeAccount as Exchange } from '../api/exchanges'
import ExchangeTypeSelect from '../components/ExchangeTypeSelect.vue'

const accounts = ref<Exchange[]>([])
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const testingId = ref<string | null>(null)
const testResult = ref<{ id: string; connected: boolean; error?: string } | null>(null)

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
  } catch {
    testResult.value = { id, connected: false, error: '请求失败' }
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
      <button class="btn-primary" @click="openCreate">添加交易所</button>
    </header>

    <div v-if="showForm" class="modal-overlay" @click.self="showForm = false">
      <div class="modal">
        <h3>{{ editId ? '编辑交易所' : '添加交易所' }}</h3>
        <input v-model="formLabel" placeholder="名称（如：币安主账户）" class="input" />
        <ExchangeTypeSelect v-model="formExchangeType" :disabled="!!editId" />
        <input v-model="formApiKey" :placeholder="editId ? '留空则不修改' : 'API Key'" type="password" class="input" />
        <input v-model="formSecretKey" :placeholder="editId ? '留空则不修改' : 'Secret Key'" type="password" class="input" />
        <input v-model="formPassphrase" placeholder="Passphrase（选填）" type="password" class="input" />
        <label class="checkbox-label">
          <input v-model="formIsTestnet" type="checkbox" />
          测试网
        </label>
        <div class="modal-actions">
          <button class="btn-secondary" @click="showForm = false">取消</button>
          <button class="btn-primary" @click="save">保存</button>
        </div>
      </div>
    </div>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th>名称</th>
          <th>交易所</th>
          <th>模式</th>
          <th>状态</th>
          <th>最近测试</th>
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
          <td>{{ a.lastTestedAt ? new Date(a.lastTestedAt).toLocaleDateString() : '-' }}</td>
          <td>{{ new Date(a.createdAt).toLocaleDateString() }}</td>
          <td class="actions">
            <button class="btn-small" :disabled="testingId === a.id" @click="testConnection(a.id)">
              {{ testingId === a.id ? '测试中...' : '测试连接' }}
            </button>
            <button class="btn-small" @click="openEdit(a)">编辑</button>
            <button class="btn-small btn-danger" @click="remove(a.id)">删除</button>
          </td>
        </tr>
        <tr v-if="testResult">
          <td colspan="7">
            <span :class="testResult.connected ? 'test-ok' : 'test-fail'">
              {{ testResult.connected ? '✓ 连接成功' : `✗ 连接失败${testResult.error ? ': ' + testResult.error : ''}` }}
            </span>
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
.page-header h2 { margin: 0; color: #e2e8f0; }
.btn-primary { padding: 0.5rem 1rem; background: #38bdf8; color: #0f172a; border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-primary:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; }
.btn-small { padding: 0.25rem 0.75rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
.btn-small:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-danger { color: #ef4444; border-color: #ef4444; background: transparent; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; }
.table th { color: #94a3b8; font-weight: 600; }
.actions { display: flex; gap: 0.5rem; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.test-ok { color: #22c55e; font-weight: 600; }
.test-fail { color: #ef4444; font-weight: 600; }
.modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; justify-content: center; align-items: center; z-index: 100; }
.modal { background: #1e293b; padding: 2rem; border-radius: 8px; width: 100%; max-width: 400px; }
.modal h3 { margin: 0 0 1rem; color: #e2e8f0; }
.input { width: 100%; padding: 0.75rem; margin-bottom: 1rem; border: 1px solid #334155; border-radius: 4px; background: #0f172a; color: #e2e8f0; box-sizing: border-box; }
.modal select.input { cursor: pointer; }
.checkbox-label { display: flex; align-items: center; gap: 0.5rem; color: #e2e8f0; margin-bottom: 1rem; cursor: pointer; }
.checkbox-label input { width: auto; margin: 0; }
.modal-actions { display: flex; justify-content: flex-end; gap: 0.5rem; }
</style>
