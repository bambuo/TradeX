<script setup lang="ts">
import { ref, onErrorCaptured } from 'vue'

const error = ref<Error | null>(null)
const errorInfo = ref('')

onErrorCaptured((err: Error, _instance, info: string) => {
  error.value = err
  errorInfo.value = info
  return false
})

function reset() {
  error.value = null
  errorInfo.value = ''
}
</script>

<template>
  <div v-if="error" class="error-boundary">
    <div class="error-card">
      <div class="error-icon">!</div>
      <h2>页面出现错误</h2>
      <p class="error-message">{{ error.message }}</p>
      <p v-if="errorInfo" class="error-info">{{ errorInfo }}</p>
      <div class="error-actions">
        <a-button type="primary" @click="reset">
          <template #icon><icon-refresh /></template>
          重试
        </a-button>
        <a-button @click="$router.push('/')">
          <template #icon><icon-home /></template>
          返回首页
        </a-button>
      </div>
    </div>
  </div>
  <slot v-else />
</template>

<style scoped>
.error-boundary {
  display: flex;
  justify-content: center;
  align-items: center;
  min-height: 60vh;
  padding: 2rem;
}
.error-card {
  background: rgba(255,255,255,0.55);
  border: 1px solid #7f1d1d;
  border-radius: 6px;
  padding: 2rem;
  max-width: 480px;
  width: 100%;
  text-align: center;
}
.error-icon {
  width: 48px;
  height: 48px;
  border-radius: 50%;
  background: #7f1d1d;
  color: #fca5a5;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0 auto 1rem;
}
h2 { color: #fca5a5; margin: 0 0 0.5rem; font-size: 1.1rem; }
.error-message { color: var(--text-muted); font-size: 0.9rem; margin: 0 0 0.25rem; }
.error-info { color: var(--text-muted); font-size: 0.8rem; margin: 0 0 1.5rem; }
.error-actions { display: flex; gap: 0.75rem; justify-content: center; }
</style>
