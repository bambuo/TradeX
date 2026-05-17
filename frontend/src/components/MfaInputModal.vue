<script setup lang="ts">
import { ref } from 'vue'
import TotpInputDigits from './login/TotpInputDigits.vue'

const visible = ref(false)
const code = ref('')
const errorMessage = ref('')

let resolvePromise: ((value: string) => void) | null = null

function requestCode(previousError?: string): Promise<string> {
  visible.value = true
  code.value = ''
  errorMessage.value = previousError ?? ''
  return new Promise<string>(resolve => {
    resolvePromise = resolve
  })
}

function confirm() {
  if (code.value.length !== 6) return
  visible.value = false
  resolvePromise?.(code.value)
  resolvePromise = null
}

function cancel() {
  visible.value = false
  resolvePromise?.('')
  resolvePromise = null
}

defineExpose({ requestCode })
</script>

<template>
  <a-modal
    :visible="visible"
    title="MFA 验证"
    :mask-closable="false"
    :footer="false"
    :unmount-on-close="true"
    @cancel="cancel"
  >
    <div class="mfa-body">
      <p class="mfa-desc">请输入身份验证器中的 6 位代码</p>

      <TotpInputDigits v-model="code" :error="errorMessage" @submit="confirm" />

      <p v-if="errorMessage" class="mfa-error">{{ errorMessage }}</p>

      <div class="mfa-footer">
        <a-button size="small" @click="cancel">取消</a-button>
        <a-button
          type="primary"
          size="small"
          :disabled="code.length !== 6"
          @click="confirm"
        >验证</a-button>
      </div>
    </div>
  </a-modal>
</template>

<style scoped>
.mfa-body {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 1rem;
  padding: 0.5rem 0;
}
.mfa-desc {
  color: var(--text-muted);
  font-size: 0.9rem;
  margin: 0;
}
.mfa-error {
  color: #f87171;
  font-size: 0.85rem;
  margin: -0.5rem 0 0;
}
.mfa-footer {
  display: flex;
  gap: 0.75rem;
}
</style>
