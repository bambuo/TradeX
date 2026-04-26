<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import client from '../api/client'

const router = useRouter()
const auth = useAuthStore()

const step = ref<'login' | 'mfa' | 'recovery' | 'mfa-setup'>('login')
const username = ref('')
const password = ref('')
const totpCode = ref('')
const recoveryCode = ref('')
const error = ref('')
const loading = ref(false)

// MFA 绑定流程状态
const mfaSetupSecret = ref('')
const mfaSetupQrUrl = ref('')

async function handleLogin() {
  error.value = ''
  loading.value = true
  try {
    const result = await auth.login(username.value, password.value)
    if (result.mfaRequired) {
      step.value = 'mfa'
    } else if (result.mfaSetupRequired) {
      // 临时将 mfaToken 存入 localStorage 供 axios client 使用
      localStorage.setItem('accessToken', result.mfaToken || '')
      step.value = 'mfa-setup'
      await loadMfaSetup()
    } else {
      router.push('/')
    }
  } catch (err: any) {
    error.value = err?.response?.data?.message || '登录失败，请检查用户名和密码'
  } finally {
    loading.value = false
  }
}

async function loadMfaSetup() {
  try {
    const { data } = await client.post('/auth/mfa/setup')
    mfaSetupSecret.value = data.secretKey
    mfaSetupQrUrl.value = data.qrCodeImage
  } catch (err: any) {
    error.value = err?.response?.data?.message || '无法获取 MFA 配置'
  }
}

async function handleVerifyMfaSetup() {
  error.value = ''
  loading.value = true
  try {
    const { data } = await client.post('/auth/mfa/verify', { code: totpCode.value })
    localStorage.setItem('accessToken', data.accessToken)
    localStorage.setItem('refreshToken', data.refreshToken)
    auth.loadFromStorage()
    router.push('/')
  } catch (err: any) {
    error.value = err?.response?.data?.message || 'MFA 验证码错误'
  } finally {
    loading.value = false
  }
}

async function handleVerifyMfa() {
  error.value = ''
  loading.value = true
  try {
    await auth.verifyMfa(totpCode.value)
    loadFromToken()
    router.push('/')
  } catch (err: any) {
    error.value = err?.response?.data?.message || 'MFA 验证失败'
  } finally {
    loading.value = false
  }
}

async function handleRecoveryCode() {
  error.value = ''
  loading.value = true
  try {
    await auth.verifyMfaWithRecoveryCode(recoveryCode.value)
    loadFromToken()
    router.push('/')
  } catch (err: any) {
    error.value = err?.response?.data?.message || '恢复码无效'
  } finally {
    loading.value = false
  }
}

function loadFromToken() {
  const token = localStorage.getItem('accessToken')
  if (token) {
    try {
      const payload = JSON.parse(atob(token.split('.')[1]))
      auth['user'] = {
        id: payload.nameidentifier || payload.sub,
        username: payload.name || payload.unique_name,
        role: payload.role || 'Viewer',
        email: '',
        isMfaEnabled: payload.mfa === 'true'
      }
    } catch { /* ignore */ }
  }
}

function switchToRecovery() {
  step.value = 'recovery'
  error.value = ''
}

function switchToMfa() {
  step.value = 'mfa'
  error.value = ''
}
</script>

<template>
  <div class="login-container">
    <!-- 登录表单 -->
    <form v-if="step === 'login'" class="login-form" @submit.prevent="handleLogin">
      <h1>TradeX</h1>
      <p class="subtitle">多交易所现货交易系统</p>
      <div v-if="error" class="error">{{ error }}</div>
      <input v-model="username" placeholder="用户名" required />
      <input v-model="password" type="password" placeholder="密码" required />
      <AppButton type="submit" variant="primary" icon="login" :disabled="loading">{{ loading ? '登录中...' : '登录' }}</AppButton>
      <div class="login-footer">
        <p>管理系统</p>
      </div>
    </form>

    <!-- MFA 验证（已有 MFA 的用户） -->
    <form v-else-if="step === 'mfa'" class="login-form" @submit.prevent="handleVerifyMfa">
      <h1>MFA 验证</h1>
      <p class="subtitle">请输入身份验证器中的 6 位代码</p>
      <div v-if="error" class="error">{{ error }}</div>
      <input v-model="totpCode" placeholder="6 位验证码" maxlength="6" required />
      <AppButton type="submit" variant="primary" icon="shield" :disabled="loading">{{ loading ? '验证中...' : '验证' }}</AppButton>
      <AppButton variant="ghost" icon="key" @click="switchToRecovery">使用恢复码</AppButton>
    </form>

    <!-- MFA 绑定流程（首次登录/无 MFA 的用户） -->
    <div v-else-if="step === 'mfa-setup'" class="login-form">
      <h1>绑定双重认证</h1>
      <div v-if="error" class="error">{{ error }}</div>

      <template v-if="mfaSetupQrUrl">
        <p class="subtitle">使用身份验证器应用（如 Google Authenticator）扫描以下二维码：</p>
        <div class="qr-section">
          <img :src="mfaSetupQrUrl" alt="MFA QR Code" class="qr" />
          <div class="secret-box">
            <span class="label">密钥：</span>
            <code>{{ mfaSetupSecret }}</code>
          </div>
        </div>
        <p class="subtitle">扫描完成后，输入应用中的 6 位验证码：</p>
        <input v-model="totpCode" placeholder="6 位验证码" maxlength="6" class="totp-input" />
        <AppButton variant="primary" icon="shield" :disabled="loading || !totpCode" @click="handleVerifyMfaSetup">{{ loading ? '验证中...' : '确认并绑定' }}</AppButton>
      </template>

      <div v-else class="loading-state">
        <div class="spinner" />
        <p>正在获取 MFA 配置...</p>
      </div>
    </div>

    <!-- 恢复码验证 -->
    <form v-else-if="step === 'recovery'" class="login-form" @submit.prevent="handleRecoveryCode">
      <h1>恢复码验证</h1>
      <p class="subtitle">输入一个恢复码（格式：XXXX-XXXX）</p>
      <div v-if="error" class="error">{{ error }}</div>
      <input v-model="recoveryCode" placeholder="XXXX-XXXX" required />
      <AppButton type="submit" variant="primary" icon="key" :disabled="loading">{{ loading ? '验证中...' : '验证' }}</AppButton>
      <AppButton variant="ghost" icon="shield" @click="switchToMfa">使用 TOTP 验证码</AppButton>
    </form>
  </div>
</template>

<style scoped>
.login-container {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 100vh;
  background: #0f172a;
}
.login-form {
  background: #1e293b;
  padding: 2rem;
  border-radius: 8px;
  width: 100%;
  max-width: 420px;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}
h1 { margin: 0; color: #38bdf8; text-align: center; }
.subtitle { color: #94a3b8; text-align: center; font-size: 0.9rem; margin: 0; }
input {
  padding: 0.75rem;
  border: 1px solid #334155;
  border-radius: 4px;
  background: #0f172a;
  color: #e2e8f0;
}
.totp-input {
  text-align: center;
  font-size: 1.25rem;
  letter-spacing: 0.25em;
}
button {
  padding: 0.75rem;
  background: #38bdf8;
  color: #0f172a;
  border: none;
  border-radius: 4px;
  font-weight: 600;
  cursor: pointer;
}
button:disabled { opacity: 0.5; }
.link-btn {
  background: transparent;
  color: #38bdf8;
  text-decoration: underline;
  font-weight: 400;
}
.error { color: #ef4444; font-size: 0.9rem; text-align: center; }
.qr-section {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 0.75rem;
}
.qr { width: 160px; height: 160px; border-radius: 4px; }
.secret-box { display: flex; align-items: center; gap: 0.5rem; }
.secret-box .label { color: #94a3b8; font-size: 0.85rem; }
.secret-box code { color: #38bdf8; font-size: 0.85rem; word-break: break-all; }
.loading-state { display: flex; flex-direction: column; align-items: center; gap: 0.75rem; padding: 2rem; }
.spinner {
  width: 32px;
  height: 32px;
  border: 3px solid #334155;
  border-top-color: #38bdf8;
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}
@keyframes spin { to { transform: rotate(360deg); } }
.loading-state p { color: #64748b; }
</style>
