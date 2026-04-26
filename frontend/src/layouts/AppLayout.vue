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
  { path: '/', label: '仪表盘', icon: 'dashboard' },
  { path: '/traders', label: '交易员', icon: 'traders' },
  { path: '/exchanges', label: '交易所', icon: 'exchange' },
  { path: '/strategies', label: '策略管理', icon: 'strategy' },
  { path: '/audit-logs', label: '审计日志', icon: 'audit' },
  { path: '/notifications', label: '通知渠道', icon: 'notification' },
  { path: '/users', label: '用户管理', icon: 'users' },
]

const navIconPaths: Record<string, string[]> = {
  dashboard: [
    'M4 5.5A1.5 1.5 0 0 1 5.5 4h3A1.5 1.5 0 0 1 10 5.5v3A1.5 1.5 0 0 1 8.5 10h-3A1.5 1.5 0 0 1 4 8.5v-3Z',
    'M14 5.5A1.5 1.5 0 0 1 15.5 4h3A1.5 1.5 0 0 1 20 5.5v3a1.5 1.5 0 0 1-1.5 1.5h-3A1.5 1.5 0 0 1 14 8.5v-3Z',
    'M4 15.5A1.5 1.5 0 0 1 5.5 14h3a1.5 1.5 0 0 1 1.5 1.5v3A1.5 1.5 0 0 1 8.5 20h-3A1.5 1.5 0 0 1 4 18.5v-3Z',
    'M14 15.5a1.5 1.5 0 0 1 1.5-1.5h3a1.5 1.5 0 0 1 1.5 1.5v3a1.5 1.5 0 0 1-1.5 1.5h-3a1.5 1.5 0 0 1-1.5-1.5v-3Z'
  ],
  traders: [
    'M16 20v-1.5A3.5 3.5 0 0 0 12.5 15h-5A3.5 3.5 0 0 0 4 18.5V20',
    'M10 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Z',
    'M20 20v-1a3 3 0 0 0-2.2-2.9',
    'M15.5 4.2a3.2 3.2 0 0 1 0 6.2'
  ],
  exchange: [
    'M4 7h13l-3-3',
    'M17 7l-3 3',
    'M20 17H7l3 3',
    'M7 17l3-3',
    'M6 12h12'
  ],
  strategy: [
    'M5 6h5',
    'M14 6h5',
    'M5 12h8',
    'M17 12h2',
    'M5 18h2',
    'M11 18h8',
    'M10 4v4',
    'M14 10v4',
    'M8 16v4'
  ],
  audit: [
    'M8 4h8l4 4v12H4V4h4Z',
    'M16 4v4h4',
    'M8 12h8',
    'M8 16h5'
  ],
  notification: [
    'M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9Z',
    'M10 20a2 2 0 0 0 4 0'
  ],
  users: [
    'M16 20v-1.5A3.5 3.5 0 0 0 12.5 15h-5A3.5 3.5 0 0 0 4 18.5V20',
    'M10 11a3.5 3.5 0 1 0 0-7 3.5 3.5 0 0 0 0 7Z',
    'M18 11a2.5 2.5 0 1 0 0-5',
    'M21 20v-1a3 3 0 0 0-2.4-2.9'
  ]
}
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
          <span class="nav-icon-wrap" aria-hidden="true">
            <svg class="nav-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
              <path v-for="path in navIconPaths[item.icon]" :key="path" :d="path" />
            </svg>
          </span>
          <span>{{ item.label }}</span>
        </router-link>
      </nav>
      <div class="sidebar-footer">
        <div class="connection-status">
          <span class="status-dot" :class="signalr.connected.value ? 'connected' : 'disconnected'" />
          {{ signalr.connected.value ? '已连接' : '未连接' }}
        </div>
        <div class="user-name">{{ auth.user?.username }}</div>
        <AppButton class="logout-btn" variant="danger" size="sm" icon="power" :disabled="loggingOut" @click="handleLogout">
          {{ loggingOut ? '退出中...' : '退出登录' }}
        </AppButton>
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
  background: transparent;
}
.sidebar {
  width: 220px;
  margin: 1rem 0 1rem 1rem;
  border: 1px solid var(--glass-border);
  border-radius: 24px;
  background:
    linear-gradient(150deg, rgba(255, 255, 255, 0.16), rgba(255, 255, 255, 0.055) 42%, rgba(15, 23, 42, 0.32)),
    rgba(15, 23, 42, 0.26);
  box-shadow: var(--glass-shadow);
  backdrop-filter: blur(36px) saturate(180%) brightness(1.08);
  -webkit-backdrop-filter: blur(36px) saturate(180%) brightness(1.08);
  display: flex;
  flex-direction: column;
  flex-shrink: 0;
  overflow: hidden;
  position: relative;
}
.sidebar::before {
  content: '';
  position: absolute;
  inset: 0;
  pointer-events: none;
  background:
    linear-gradient(115deg, rgba(255, 255, 255, 0.28), transparent 22%, transparent 72%, rgba(255, 255, 255, 0.07)),
    radial-gradient(circle at 30% 0%, rgba(255, 255, 255, 0.18), transparent 12rem);
  opacity: 0.9;
}
.sidebar > * { position: relative; }
.logo {
  padding: 1.25rem;
  color: var(--text-primary);
  font-size: 1.25rem;
  font-weight: 700;
  cursor: pointer;
  border-bottom: 1px solid var(--glass-border);
  letter-spacing: -0.03em;
  background: linear-gradient(135deg, rgba(255, 255, 255, 0.16), rgba(56, 189, 248, 0.12), rgba(255, 255, 255, 0.035));
}
.nav {
  display: flex;
  flex-direction: column;
  padding: 0.75rem;
  gap: 0.25rem;
  flex: 1;
}
.nav-link {
  display: flex;
  align-items: center;
  gap: 0.65rem;
  padding: 0.625rem 0.75rem;
  color: var(--text-muted);
  text-decoration: none;
  border: 1px solid transparent;
  border-radius: 14px;
  font-size: 0.9rem;
  transition: all 0.15s;
}
.nav-link:hover { color: var(--text-primary); background: rgba(255, 255, 255, 0.07); border-color: var(--glass-border); }
.nav-link.active { color: var(--accent-blue); background: rgba(56, 189, 248, 0.13); border-color: rgba(56, 189, 248, 0.28); font-weight: 600; box-shadow: inset 0 1px 0 var(--glass-highlight); }
.nav-icon-wrap {
  width: 2rem;
  height: 2rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  border-radius: 12px;
  background: rgba(255, 255, 255, 0.055);
  border: 1px solid rgba(255, 255, 255, 0.10);
  color: currentColor;
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.10);
}
.nav-link.active .nav-icon-wrap {
  background: rgba(56, 189, 248, 0.18);
  border-color: rgba(56, 189, 248, 0.34);
  box-shadow: 0 0 18px rgba(56, 189, 248, 0.12), inset 0 1px 0 rgba(255, 255, 255, 0.18);
}
.nav-icon {
  width: 1.1rem;
  height: 1.1rem;
}
.sidebar-footer {
  padding: 1rem;
  border-top: 1px solid var(--glass-border);
}
.connection-status {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.75rem;
  color: var(--text-muted);
  margin-bottom: 0.5rem;
}
.status-dot {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  flex-shrink: 0;
}
.status-dot.connected { background: var(--accent-green); box-shadow: 0 0 10px var(--accent-green); }
.status-dot.disconnected { background: var(--accent-red); }
.user-name {
  color: var(--text-primary);
  font-size: 0.85rem;
  margin-bottom: 0.5rem;
  overflow: hidden;
  text-overflow: ellipsis;
}
.logout-btn {
  width: 100%;
  font-size: 0.8rem;
}
.main {
  flex: 1;
  overflow-y: auto;
  min-width: 0;
}

@media (max-width: 760px) {
  .layout { flex-direction: column; }
  .sidebar {
    width: auto;
    margin: 0.75rem;
    border-radius: 20px;
  }
  .nav {
    flex-direction: row;
    overflow-x: auto;
    padding-bottom: 0.75rem;
  }
  .nav-link { white-space: nowrap; }
  .sidebar-footer { display: none; }
}
</style>
