<script setup lang="ts">
import { ref, onMounted, onUnmounted, provide } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useSignalR } from '../composables/useSignalR'
import ErrorBoundary from '../components/ErrorBoundary.vue'
import Breadcrumb from '../components/Breadcrumb.vue'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const signalr = useSignalR()
const loggingOut = ref(false)

provide('signalr', signalr)

onMounted(async () => {
  if (auth.isAuthenticated) {
    try {
      await signalr.connect()
    } catch {
      // connection will be retried by automatic reconnect
    }
  }
})

onUnmounted(() => {
  signalr.disconnect()
})

async function handleLogout() {
  loggingOut.value = true
  try {
    signalr.disconnect()
  } catch { /* ignore */ }
  await auth.logout()
  router.push('/login')
}

const navItems = [
  { path: '/', label: '仪表盘' },
  { path: '/traders', label: '交易员' },
  { path: '/audit-logs', label: '审计日志' },
  { path: '/notifications', label: '通知渠道' },
  { path: '/users', label: '用户管理' },
]
</script>

<template>
  <div class="layout">
    <aside class="sidebar">
      <div class="logo" @click="router.push('/')">TradeX</div>
      <nav class="nav">
        <router-link
          v-for="item in navItems"
          :key="item.path"
          :to="item.path"
          class="nav-link"
          :class="{ active: route.path === item.path }"
        >
          {{ item.label }}
        </router-link>
      </nav>
      <div class="sidebar-footer">
        <div class="connection-status">
          <span class="status-dot" :class="signalr.connected.value ? 'connected' : 'disconnected'" />
          {{ signalr.connected.value ? '已连接' : '未连接' }}
        </div>
        <div class="user-name">{{ auth.user?.username }}</div>
        <button class="logout-btn" :disabled="loggingOut" @click="handleLogout">
          {{ loggingOut ? '退出中...' : '退出登录' }}
        </button>
      </div>
    </aside>
    <main class="main">
      <Breadcrumb />
      <ErrorBoundary>
        <router-view />
      </ErrorBoundary>
    </main>
  </div>
</template>

<style scoped>
.layout {
  display: flex;
  min-height: 100vh;
  background: #0f172a;
}
.sidebar {
  width: 220px;
  background: #1e293b;
  border-right: 1px solid #334155;
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
}
.logo {
  padding: 1.25rem;
  color: #38bdf8;
  font-size: 1.25rem;
  font-weight: 700;
  cursor: pointer;
  border-bottom: 1px solid #334155;
}
.nav {
  display: flex;
  flex-direction: column;
  padding: 0.75rem;
  gap: 0.25rem;
  flex: 1;
}
.nav-link {
  padding: 0.625rem 0.75rem;
  color: #94a3b8;
  text-decoration: none;
  border-radius: 6px;
  font-size: 0.9rem;
  transition: all 0.15s;
}
.nav-link:hover { color: #e2e8f0; background: #334155; }
.nav-link.active { color: #38bdf8; background: rgba(56, 189, 248, 0.1); font-weight: 600; }
.sidebar-footer {
  padding: 1rem;
  border-top: 1px solid #334155;
}
.connection-status {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.75rem;
  color: #94a3b8;
  margin-bottom: 0.5rem;
}
.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
.status-dot.connected { background: #22c55e; box-shadow: 0 0 4px #22c55e; }
.status-dot.disconnected { background: #ef4444; }
.user-name {
  color: #e2e8f0;
  font-size: 0.85rem;
  margin-bottom: 0.5rem;
  overflow: hidden;
  text-overflow: ellipsis;
}
.logout-btn {
  width: 100%;
  padding: 0.5rem;
  background: transparent;
  color: #ef4444;
  border: 1px solid #ef4444;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.8rem;
  transition: background 0.15s;
}
.logout-btn:hover { background: rgba(239, 68, 68, 0.1); }
.main {
  flex: 1;
  overflow-y: auto;
}
</style>
