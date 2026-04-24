<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import client from '../api/client'
import { resetInitCheck } from '../router'

const router = useRouter()
const step = ref<'checking' | 'form' | 'success'>('checking')
const username = ref('')
const password = ref('')
const confirmPassword = ref('')
const error = ref('')
const loading = ref(false)

onMounted(async () => {
  try {
    const { data } = await client.get('/setup/status')
    if (data.isInitialized) {
      router.replace('/login')
      return
    }
    step.value = 'form'
  } catch {
    step.value = 'form'
  }
})

async function initialize() {
  error.value = ''

  if (!username.value || username.value.length < 3) {
    error.value = '用户名至少 3 个字符'
    return
  }
  if (!password.value || password.value.length < 8) {
    error.value = '密码至少 8 个字符'
    return
  }
  if (password.value !== confirmPassword.value) {
    error.value = '两次密码输入不一致'
    return
  }

  loading.value = true
  try {
    await client.post('/setup/initialize', {
      userName: username.value,
      password: password.value
    })
    resetInitCheck()
    step.value = 'success'
  } catch (err: any) {
    error.value = err?.response?.data?.message || '初始化失败'
  } finally {
    loading.value = false
  }
}
</script>

<template>
  <div class="setup-page">
    <div class="setup-card">
      <div v-if="step === 'checking'" class="loading-state">
        <div class="spinner" />
        <p>检查系统状态...</p>
      </div>

      <div v-else-if="step === 'form'" class="setup-form">
        <h1>TradeX 初始化</h1>
        <p class="subtitle">首次部署，请创建 Super Admin 账户</p>

        <div v-if="error" class="error">{{ error }}</div>

        <input v-model="username" placeholder="Super Admin 用户名" required />
        <input v-model="password" type="password" placeholder="密码 (至少 8 位)" required />
        <input v-model="confirmPassword" type="password" placeholder="确认密码" required />

        <button :disabled="loading" @click="initialize">
          {{ loading ? '初始化中...' : '初始化系统' }}
        </button>
      </div>

      <div v-else class="success-state">
        <div class="success-icon">✓</div>
        <h2>初始化成功！</h2>
        <p>系统已就绪，请登录后完成 MFA 绑定</p>
        <button class="btn-primary" @click="router.push('/login')">前往登录</button>
      </div>
    </div>
  </div>
</template>

<style scoped>
.setup-page {
  min-height: 100vh;
  display: flex;
  justify-content: center;
  align-items: center;
  background: #0f172a;
}
.setup-card {
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 12px;
  padding: 3rem;
  width: 100%;
  max-width: 420px;
}
h1 { margin: 0 0 0.25rem; color: #e2e8f0; font-size: 1.5rem; text-align: center; }
h2 { margin: 0 0 0.5rem; color: #e2e8f0; font-size: 1.3rem; }
.subtitle { color: #94a3b8; text-align: center; margin: 0 0 1.5rem; font-size: 0.9rem; }
.setup-form, .success-state, .loading-state {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}
input {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid #334155;
  border-radius: 6px;
  background: #0f172a;
  color: #e2e8f0;
  font-size: 0.95rem;
  box-sizing: border-box;
}
input:focus { outline: none; border-color: #38bdf8; }
button {
  padding: 0.75rem;
  background: #38bdf8;
  color: #0f172a;
  border: none;
  border-radius: 6px;
  font-size: 1rem;
  font-weight: 600;
  cursor: pointer;
  margin-top: 0.5rem;
}
button:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-primary { background: #38bdf8; color: #0f172a; }
.error {
  padding: 0.75rem;
  background: rgba(239,68,68,0.1);
  color: #ef4444;
  border: 1px solid rgba(239,68,68,0.3);
  border-radius: 6px;
  font-size: 0.85rem;
  text-align: center;
}
.success-icon {
  width: 64px;
  height: 64px;
  border-radius: 50%;
  background: rgba(34,197,94,0.15);
  color: #22c55e;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.8rem;
  font-weight: bold;
  margin: 0 auto;
}
.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid #334155;
  border-top-color: #38bdf8;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
  margin: 0 auto;
}
@keyframes spin { to { transform: rotate(360deg); } }
.loading-state p { text-align: center; color: #64748b; }
</style>
