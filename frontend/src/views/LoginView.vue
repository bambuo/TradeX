<script setup lang="ts">
import { ref, computed, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import client from '../api/client'
import TotpInputDigits from '../components/login/TotpInputDigits.vue'

const router = useRouter()
const auth = useAuthStore()

type Step = 'login' | 'mfa' | 'recovery' | 'mfa-setup'
const step = ref<Step>('login')
const username = ref('')
const password = ref('')
const totpCode = ref('')
const recoveryCode = ref('')
const error = ref('')
const loading = ref(false)

const mfaSetupSecret = ref('')
const mfaSetupQrUrl = ref('')

// MFA 倒计时
const mfaCountdown = ref(300)
let countdownTimer: ReturnType<typeof setInterval> | null = null

function startCountdown(seconds: number) {
  mfaCountdown.value = seconds
  if (countdownTimer) clearInterval(countdownTimer)
  countdownTimer = setInterval(() => {
    if (mfaCountdown.value > 0) mfaCountdown.value--
    else clearInterval(countdownTimer!)
  }, 1000)
}

const countdownText = computed(() => {
  const m = Math.floor(mfaCountdown.value / 60)
  const s = mfaCountdown.value % 60
  return `${m}:${s.toString().padStart(2, '0')}`
})

const countdownUrgent = computed(() => mfaCountdown.value <= 60 && step.value !== 'login')

onUnmounted(() => {
  if (countdownTimer) clearInterval(countdownTimer)
})

async function handleLogin() {
  error.value = ''
  loading.value = true
  try {
    const result = await auth.login(username.value, password.value)
    if (result.mfaRequired) {
      step.value = 'mfa'
      startCountdown(300)
    } else if (result.mfaSetupRequired) {
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

function switchStep(s: Step) {
  step.value = s
  error.value = ''
  totpCode.value = ''
}
</script>

<template>
  <div class="login-page">
    <div class="login-wrapper">
      <div class="brand">
        <div class="brand-icon">
          <icon-safe :size="32" />
        </div>
        <h1 class="brand-title">TradeX</h1>
        <p class="brand-desc">多交易所现货自动交易系统</p>
      </div>

      <a-card class="login-card" :bordered="false">

        <div v-if="error" class="error-banner">{{ error }}</div>

        <Transition name="fade-slide" mode="out-in">
          <!-- 凭据登录 -->
          <div v-if="step === 'login'" key="login">
            <a-input
              v-model="username"
              placeholder="用户名"
              size="large"
              class="form-input"
            >
              <template #prefix><icon-user /></template>
            </a-input>
            <a-input-password
              v-model="password"
              placeholder="密码"
              size="large"
              class="form-input"
              @keyup.enter="handleLogin"
            >
              <template #prefix><icon-lock /></template>
            </a-input-password>
            <a-button
              type="primary"
              size="large"
              long
              :loading="loading"
              @click="handleLogin"
            >{{ loading ? '登录中...' : '登录' }}</a-button>
          </div>

          <!-- MFA 验证 -->
          <div v-else-if="step === 'mfa'" key="mfa" class="mfa-step">
            <p class="mfa-hint">请输入身份验证器中的 6 位代码</p>
            <TotpInputDigits v-model="totpCode" :error="error" @submit="handleVerifyMfa" />
            <a-button
              type="primary"
              size="large"
              long
              :disabled="totpCode.length !== 6"
              :loading="loading"
              @click="handleVerifyMfa"
            >{{ loading ? '验证中...' : '验证' }}</a-button>
            <a-button type="text" long @click="switchStep('recovery')">
              <template #icon><icon-key /></template>
              使用恢复码
            </a-button>
          </div>

          <!-- MFA 绑定 -->
          <div v-else-if="step === 'mfa-setup'" key="mfa-setup" class="mfa-step">
            <template v-if="mfaSetupQrUrl">
              <div class="qr-wrap">
                <img :src="mfaSetupQrUrl" alt="MFA QR" class="qr-img" />
              </div>
              <div class="secret-row">
                <span class="secret-lbl">密钥</span>
                <a-tag color="arcoblue" class="secret-tag">{{ mfaSetupSecret }}</a-tag>
              </div>
              <p class="mfa-hint">扫描二维码后，输入应用中的 6 位代码</p>
              <TotpInputDigits v-model="totpCode" :error="error" @submit="handleVerifyMfaSetup" />
              <a-button
                type="primary"
                size="large"
                long
                :disabled="totpCode.length !== 6"
                :loading="loading"
                @click="handleVerifyMfaSetup"
              >{{ loading ? '验证中...' : '确认并绑定' }}</a-button>
            </template>
            <div v-else class="loading-state">
              <a-spin />
              <p>正在获取 MFA 配置...</p>
            </div>
          </div>

          <!-- 恢复码 -->
          <div v-else-if="step === 'recovery'" key="recovery" class="mfa-step">
            <p class="mfa-hint">输入一个恢复码（格式：XXXX-XXXX）</p>
            <a-input
              v-model="recoveryCode"
              placeholder="XXXX-XXXX"
              size="large"
              class="form-input"
              @keyup.enter="handleRecoveryCode"
            >
              <template #prefix><icon-key /></template>
            </a-input>
            <a-button
              type="primary"
              size="large"
              long
              :disabled="!recoveryCode"
              :loading="loading"
              @click="handleRecoveryCode"
            >{{ loading ? '验证中...' : '验证' }}</a-button>
            <a-button type="text" long @click="switchStep('mfa')">
              <template #icon><icon-safe /></template>
              使用 TOTP 验证码
            </a-button>
          </div>
        </Transition>

        <!-- 倒计时（仅 MFA 验证步骤） -->
        <div v-if="step === 'mfa' || step === 'mfa-setup'" class="countdown-bar">
          <span :class="['countdown', { urgent: countdownUrgent }]">
            <icon-safe /> {{ countdownText }}
          </span>
          <a-button type="text" size="small" @click="switchStep('login')">返回登录</a-button>
        </div>
      </a-card>
    </div>
  </div>
</template>

<style scoped>
.login-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, #0b0f1e 0%, #1a1a2e 50%, #16213e 100%);
  padding: 24px;
}

.login-wrapper {
  width: 100%;
  max-width: 420px;
}

/* ── 品牌区 ── */
.brand {
  text-align: center;
  margin-bottom: 32px;
}

.brand-icon {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 56px;
  height: 56px;
  margin: 0 auto 16px;
  background: linear-gradient(135deg, #165DFF 0%, #4080FF 100%);
  border-radius: 14px;
  color: #fff;
  box-shadow: 0 8px 24px rgba(22, 93, 255, 0.3);
}

.brand-title {
  font-size: 24px;
  font-weight: 700;
  color: #fff;
  margin: 0 0 6px;
  letter-spacing: 1px;
}

.brand-desc {
  font-size: 13px;
  color: rgba(255, 255, 255, 0.5);
  margin: 0;
}

/* ── 登录卡片 ── */
.login-card {
  border-radius: 12px;
  box-shadow: 0 8px 40px rgba(0, 0, 0, 0.3);
  background: var(--color-bg-1, #fff);
}

.login-card :deep(.arco-card-body) {
  padding: 24px;
}

/* ── 表单元素 ── */
.form-input {
  margin-bottom: 12px;
}

.mfa-step {
  display: flex;
  flex-direction: column;
  gap: 12px;
  align-items: center;
}

.mfa-hint {
  font-size: 13px;
  color: var(--color-text-2, #4e5969);
  text-align: center;
  margin: 0;
}

.error-banner {
  color: #f87171;
  font-size: 13px;
  text-align: center;
  margin-bottom: 12px;
  padding: 8px 12px;
  background: rgba(248, 113, 113, 0.08);
  border-radius: 6px;
}

/* ── 二维码 ── */
.qr-wrap {
  padding: 12px;
  background: #fff;
  border-radius: 12px;
  border: 1px solid var(--color-border-2, #e5e8ef);
}

.qr-img {
  display: block;
  width: 180px;
  height: 180px;
}

.secret-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.secret-lbl {
  font-size: 13px;
  color: var(--color-text-3, #86909c);
}

.secret-tag {
  font-family: "SF Mono", Menlo, Monaco, Consolas, monospace;
  user-select: all;
  font-size: 12px;
}

/* ── 倒计时 ── */
.countdown-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-top: 12px;
  margin-top: 12px;
  border-top: 1px solid var(--color-border-2, #e5e8ef);
}

.countdown {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 12px;
  color: var(--color-text-3, #86909c);
  font-family: "SF Mono", Menlo, Monaco, Consolas, monospace;
}

.countdown.urgent {
  color: rgb(var(--danger-6));
  font-weight: 600;
  animation: blink 1s ease-in-out infinite;
}

@keyframes blink {
  0%, 100% { opacity: 1; }
  50% { opacity: 0.4; }
}

.loading-state {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 24px;
}

.loading-state p {
  color: var(--color-text-3, #86909c);
  margin: 0;
}

/* ── 步骤动画 ── */
.fade-slide-enter-active,
.fade-slide-leave-active {
  transition: all 0.25s cubic-bezier(0.4, 0, 0.2, 1);
}

.fade-slide-enter-from {
  opacity: 0;
  transform: translateY(12px);
}

.fade-slide-leave-to {
  opacity: 0;
  transform: translateY(-12px);
}

/* ── 响应式 ── */
@media (max-width: 480px) {
  .login-page {
    padding: 16px;
    background: var(--color-bg-2, #f2f3f5);
  }

  .brand {
    margin-bottom: 24px;
  }

  .brand-icon {
    width: 48px;
    height: 48px;
  }

  .brand-title {
    font-size: 20px;
  }

  .brand-desc {
    font-size: 12px;
  }

  .login-card {
    box-shadow: none;
    border-radius: 8px;
  }

  .login-card :deep(.arco-card-body) {
    padding: 20px 16px;
  }

  .qr-img {
    width: 160px;
    height: 160px;
  }
}
</style>
