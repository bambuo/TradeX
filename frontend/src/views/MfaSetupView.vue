<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { authApi } from '../api/auth'

const router = useRouter()

const secret = ref('')
const qrCodeUrl = ref('')
const totpCode = ref('')
const error = ref('')
const loading = ref(false)
const step = ref<'setup' | 'verify' | 'done'>('setup')

onMounted(async () => {
  try {
    const { data } = await authApi.setupMfa()
    secret.value = data.secretKey
    qrCodeUrl.value = data.qrCodeImage || data.qrCodeUrl
    step.value = 'setup'
  } catch {
    error.value = '无法获取 MFA 配置信息'
  }
})

async function handleVerify() {
  error.value = ''
  loading.value = true
  try {
    await authApi.verifyMfaSetup(totpCode.value)
    step.value = 'done'
  } catch (err: any) {
    error.value = err?.response?.data?.message || '验证码错误'
  } finally {
    loading.value = false
  }
}

function goToDashboard() {
  router.push('/')
}
</script>

<template>
  <div class="mfa-setup">
    <div class="card">
      <h1>设置双重认证</h1>

      <div v-if="step === 'setup'" class="step">
        <p class="desc">使用身份验证器应用（如 Google Authenticator、Authy）扫描以下二维码或手动输入密钥：</p>
        <div class="qr-section">
          <img v-if="qrCodeUrl" :src="qrCodeUrl" alt="MFA QR Code" class="qr" />
          <div class="secret-box">
            <span class="label">密钥：</span>
            <code>{{ secret }}</code>
          </div>
        </div>
        <AppButton variant="primary" icon="test" @click="step = 'verify'">我已设置</AppButton>
      </div>

      <div v-else-if="step === 'verify'" class="step">
        <p class="desc">输入身份验证器中的 6 位验证码以确认设置：</p>
        <div v-if="error" class="error">{{ error }}</div>
        <input v-model="totpCode" placeholder="6 位验证码" maxlength="6" class="input" @keyup.enter="handleVerify" />
        <AppButton variant="primary" icon="shield" :disabled="loading" @click="handleVerify">{{ loading ? '验证中...' : '确认并启用' }}</AppButton>
      </div>

      <div v-else class="step">
        <div class="success-icon">✓</div>
        <p class="desc">双重认证已成功启用！</p>
        <AppButton variant="primary" icon="home" @click="goToDashboard">返回仪表盘</AppButton>
      </div>
    </div>
  </div>
</template>

<style scoped>
.mfa-setup {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 100vh;
  background: #0f172a;
}
.card {
  background: #1e293b;
  padding: 2rem;
  border-radius: 8px;
  width: 100%;
  max-width: 480px;
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
}
h1 {
  margin: 0;
  color: #e2e8f0;
  text-align: center;
  font-size: 1.25rem;
}
.desc { color: #94a3b8; text-align: center; font-size: 0.9rem; margin: 0; }
.qr-section { display: flex; flex-direction: column; align-items: center; gap: 1rem; }
.qr { width: 180px; height: 180px; border-radius: 4px; }
.secret-box { display: flex; align-items: center; gap: 0.5rem; }
.secret-box .label { color: #94a3b8; font-size: 0.85rem; }
.secret-box code { color: #38bdf8; font-size: 0.85rem; word-break: break-all; }
.input {
  width: 100%;
  padding: 0.75rem;
  border: 1px solid #334155;
  border-radius: 4px;
  background: #0f172a;
  color: #e2e8f0;
  text-align: center;
  font-size: 1.25rem;
  letter-spacing: 0.25em;
  box-sizing: border-box;
}
.btn-primary {
  padding: 0.75rem;
  background: #38bdf8;
  color: #0f172a;
  border: none;
  border-radius: 4px;
  font-weight: 600;
  cursor: pointer;
  width: 100%;
}
.btn-primary:disabled { opacity: 0.5; }
.error { color: #ef4444; font-size: 0.9rem; text-align: center; }
.step { display: flex; flex-direction: column; align-items: center; gap: 1rem; }
.success-icon {
  width: 48px;
  height: 48px;
  border-radius: 50%;
  background: #22c55e;
  color: #fff;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.5rem;
  font-weight: 700;
}
</style>
