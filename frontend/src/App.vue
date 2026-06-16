<script setup lang="ts">
import { ref, onMounted, onErrorCaptured, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from './stores/auth'

const auth = useAuthStore()
const router = useRouter()
const hasError = ref(false)
const errorMessage = ref('')

onMounted(() => auth.loadFromStorage())

// 捕获子组件渲染错误，防止崩溃污染整个应用
onErrorCaptured((err: unknown) => {
  hasError.value = true
  errorMessage.value = err instanceof Error ? err.message : String(err)
  console.error('[App] error captured:', err)
  return false // 阻止错误继续向上传播
})

// 路由切换时清除错误状态
watch(() => router.currentRoute.value.path, () => {
  hasError.value = false
  errorMessage.value = ''
})
</script>

<template>
  <div v-if="hasError" class="global-error">
    <div class="error-card">
      <div class="error-icon">!</div>
      <h2>页面出现错误</h2>
      <p class="error-message">{{ errorMessage }}</p>
      <div class="error-actions">
        <a-button type="primary" @click="hasError = false">
          <template #icon><icon-refresh /></template>
          重试
        </a-button>
        <a-button @click="router.push('/')">
          <template #icon><icon-home /></template>
          返回首页
        </a-button>
      </div>
    </div>
  </div>
  <router-view v-else />
</template>

<style scoped>
.global-error {
  display: flex;
  align-items: center;
  justify-content: center;
  min-height: 100vh;
  background: var(--color-fill-1);
}
.error-card {
  text-align: center;
  max-width: 400px;
  padding: 40px;
}
.error-icon {
  width: 56px;
  height: 56px;
  border-radius: 50%;
  background: rgb(var(--red-6));
  color: #fff;
  font-size: 28px;
  font-weight: 700;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  margin-bottom: 16px;
}
.error-message {
  color: var(--color-text-3);
  font-size: 13px;
  margin: 8px 0 24px;
  word-break: break-all;
}
.error-actions {
  display: flex;
  gap: 12px;
  justify-content: center;
}
</style>
