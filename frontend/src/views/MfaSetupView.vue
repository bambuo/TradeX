<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import { authApi } from '../api/auth'
import TotpInputDigits from '../components/login/TotpInputDigits.vue'

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
</script>

<template>
  <div class="mfa-page">
    <div class="mfa-wrapper">
      <div class="brand">
        <div class="brand-icon">
          <icon-safe :size="32" />
        </div>
        <h1 class="brand-title">TradeX</h1>
        <p class="brand-desc">双重认证设置</p>
      </div>

      <a-card class="mfa-card" :bordered="false">

        <div v-if="error && step !== 'done'" class="error-banner">{{ error }}</div>

        <!-- Step 1: 显示二维码 -->
        <div v-if="step === 'setup'" class="step-body">
          <p class="step-desc">使用身份验证器应用扫描二维码或手动输入密钥</p>

          <div class="qr-wrap" v-if="qrCodeUrl">
            <img :src="qrCodeUrl" alt="MFA QR Code" class="qr-img" />
          </div>

          <div class="secret-row">
            <span class="secret-lbl">密钥</span>
            <a-tag color="arcoblue" class="secret-tag">{{ secret }}</a-tag>
          </div>

          <a-button type="primary" size="large" long @click="step = 'verify'">
            <template #icon><icon-check-circle /></template>
            我已设置
          </a-button>
        </div>

        <!-- Step 2: 输入验证码 -->
        <div v-else-if="step === 'verify'" class="step-body">
          <p class="step-desc">输入身份验证器中的 6 位代码以确认设置</p>

          <TotpInputDigits v-model="totpCode" :error="error" @submit="handleVerify" />

          <a-button
            type="primary"
            size="large"
            long
            :disabled="totpCode.length !== 6"
            :loading="loading"
            @click="handleVerify"
          >{{ loading ? '验证中...' : '确认并启用' }}</a-button>
        </div>

        <!-- Done -->
        <div v-else class="step-body">
          <div class="success-icon">
            <icon-check-circle-fill :size="48" />
          </div>
          <h3>双重认证已启用</h3>
          <a-button type="primary" size="large" long @click="router.push('/')">
            <template #icon><icon-home /></template>
            返回仪表盘
          </a-button>
        </div>
      </a-card>
    </div>
  </div>
</template>

<style scoped>
.mfa-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, #0b0f1e 0%, #1a1a2e 50%, #16213e 100%);
  padding: 24px;
}

.mfa-wrapper {
  width: 100%;
  max-width: 460px;
}

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

.mfa-card {
  border-radius: 12px;
  box-shadow: 0 8px 40px rgba(0, 0, 0, 0.3);
}

.mfa-card :deep(.arco-card-body) {
  padding: 24px;
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

.step-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 16px;
}

.step-desc {
  font-size: 13px;
  color: var(--color-text-2);
  text-align: center;
  margin: 0;
  line-height: 1.5;
}

.qr-wrap {
  padding: 12px;
  background: #fff;
  border-radius: 12px;
  border: 1px solid var(--color-border-2);
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
  color: var(--color-text-3);
}

.secret-tag {
  font-family: "SF Mono", Menlo, Monaco, Consolas, monospace;
  user-select: all;
  font-size: 12px;
}

.success-icon {
  color: rgb(var(--success-6));
}

.step-body h3 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}

@media (max-width: 480px) {
  .mfa-page {
    padding: 16px;
    background: var(--color-bg-2);
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

  .mfa-card {
    box-shadow: none;
    border-radius: 8px;
  }

  .mfa-card :deep(.arco-card-body) {
    padding: 20px 16px;
  }

  .qr-img {
    width: 160px;
    height: 160px;
  }
}
</style>
