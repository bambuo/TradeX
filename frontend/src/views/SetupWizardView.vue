<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRouter } from 'vue-router'
import client from '../api/client'
import { resetInitCheck } from '../router'

const router = useRouter()
const step = ref<'checking' | 'form' | 'success'>('checking')
const form = ref({ name: '', password: '', confirmPassword: '' })
const username = computed(() => form.value.name)
const password = computed(() => form.value.password)
const confirmPassword = computed(() => form.value.confirmPassword)
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

function validatePassword(pw: string): boolean {
  return pw.length >= 8 && /[a-zA-Z]/.test(pw) && /[0-9]/.test(pw)
}

const passwordValid = () => password.value.length === 0 || validatePassword(password.value)
const confirmValid = () => password.value === confirmPassword.value
const canSubmit = () => username.value.length >= 3 && validatePassword(password.value) && confirmValid()

async function initialize() {
  error.value = ''
  if (!canSubmit()) {
    if (username.value.length < 3) error.value = '用户名至少 3 个字符'
    else if (!validatePassword(password.value)) error.value = '密码至少 8 位，需包含字母和数字'
    else if (!confirmValid()) error.value = '两次密码输入不一致'
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
    <div class="setup-wrapper">
      <div class="brand">
        <div class="brand-icon">
          <icon-safe :size="32" />
        </div>
        <h1 class="brand-title">TradeX</h1>
        <p class="brand-desc">系统初始化</p>
      </div>

      <a-card class="setup-card" :bordered="false">

        <div v-if="step === 'checking'" class="loading-state">
          <a-spin />
          <p>检查系统状态...</p>
        </div>

        <div v-else-if="step === 'form'">
          <div v-if="error" class="error-banner">{{ error }}</div>

          <a-form :model="form" layout="vertical">
            <a-form-item label="用户名">
              <a-input
                v-model="form.name"
                placeholder="Super Admin 用户名"
                size="large"
                :max-length="64"
                allow-clear
              />
            </a-form-item>

            <a-form-item label="密码">
              <a-input-password
                v-model="form.password"
                placeholder="至少 8 位，需包含字母和数字"
                size="large"
                allow-clear
              />
              <template #extra>
                <span v-if="password.length > 0 && !passwordValid()" class="field-error">
                  密码需至少 8 位，包含字母和数字
                </span>
                <span v-else class="field-hint">至少 8 位，包含字母和数字</span>
              </template>
            </a-form-item>

            <a-form-item label="确认密码">
              <a-input-password
                v-model="form.confirmPassword"
                placeholder="再次输入密码"
                size="large"
                allow-clear
              />
              <template #extra>
                <span v-if="confirmPassword.length > 0 && !confirmValid()" class="field-error">
                  两次密码不一致
                </span>
              </template>
            </a-form-item>

            <a-form-item>
              <a-button
                type="primary"
                size="large"
                long
                :loading="loading"
                :disabled="!canSubmit()"
                @click="initialize"
              >{{ loading ? '初始化中...' : '初始化系统' }}</a-button>
            </a-form-item>
          </a-form>
        </div>

        <div v-else class="success-step">
          <div class="success-icon">
            <icon-check-circle-fill :size="48" />
          </div>
          <h3>初始化成功！</h3>
          <p>系统已就绪，请登录后完成 MFA 绑定</p>
          <a-button type="primary" size="large" @click="router.push('/login')">
            <template #icon><icon-login /></template>
            前往登录
          </a-button>
        </div>
      </a-card>
    </div>
  </div>
</template>

<style scoped>
.setup-page {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: linear-gradient(135deg, #0b0f1e 0%, #1a1a2e 50%, #16213e 100%);
  padding: 24px;
}

.setup-wrapper {
  width: 100%;
  max-width: 420px;
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

.setup-card {
  border-radius: 12px;
  box-shadow: 0 8px 40px rgba(0, 0, 0, 0.3);
}

.setup-card :deep(.arco-card-body) {
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

.field-error {
  font-size: 12px;
  color: #f87171;
}

.field-hint {
  font-size: 12px;
  color: var(--color-text-3, #86909c);
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

.success-step {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 12px;
  padding: 8px 0;
}

.success-icon {
  color: rgb(var(--success-6));
}

.success-step h3 {
  margin: 0;
  font-size: 18px;
  font-weight: 600;
}

.success-step p {
  margin: 0;
  font-size: 13px;
  color: var(--color-text-3, #86909c);
}

@media (max-width: 480px) {
  .setup-page {
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

  .setup-card {
    box-shadow: none;
    border-radius: 8px;
  }

  .setup-card :deep(.arco-card-body) {
    padding: 20px 16px;
  }
}
</style>
