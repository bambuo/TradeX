<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted, provide } from 'vue'
import { useRouter, useRoute } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { useSignalR } from '../composables/useSignalR'
import ErrorBoundary from '../components/ErrorBoundary.vue'

const router = useRouter()
const route = useRoute()
const auth = useAuthStore()
const signalr = useSignalR()
const loggingOut = ref(false)

const fabX = ref(0)
const fabY = ref(0)
const fabDragging = ref(false)
const fabMoved = ref(false)
const fabEl = ref<HTMLElement | null>(null)

function onDragStart(e: MouseEvent | TouchEvent) {
  fabDragging.value = true
  fabMoved.value = false
  const el = fabEl.value
  if (!el) return
  const rect = el.getBoundingClientRect()
  const cx = 'touches' in e ? e.touches[0].clientX : e.clientX
  const cy = 'touches' in e ? e.touches[0].clientY : e.clientY
  fabX.value = cx - rect.left
  fabY.value = cy - rect.top
  el.style.position = 'fixed'
  el.style.bottom = 'auto'
  el.style.right = 'auto'
  el.style.left = rect.left + 'px'
  el.style.top = rect.top + 'px'
}

function onDragMove(e: MouseEvent | TouchEvent) {
  if (!fabDragging.value) return
  e.preventDefault()
  fabMoved.value = true
  const el = fabEl.value
  if (!el) return
  const cx = 'touches' in e ? e.touches[0].clientX : e.clientX
  const cy = 'touches' in e ? e.touches[0].clientY : e.clientY
  el.style.left = (cx - fabX.value) + 'px'
  el.style.top = (cy - fabY.value) + 'px'
}

function onDragEnd() {
  fabDragging.value = false
}

function handleFabClick() {
  if (fabMoved.value) return
  handleLogout()
}

provide('signalr', signalr)

onMounted(async () => {
  if (auth.isAuthenticated) {
    try {
      await signalr.connect()
    } catch {
      // connection will be retried by automatic reconnect
    }
  }
  document.addEventListener('mousemove', onDragMove)
  document.addEventListener('mouseup', onDragEnd)
  document.addEventListener('touchmove', onDragMove, { passive: false })
  document.addEventListener('touchend', onDragEnd)
})

onUnmounted(() => {
  signalr.disconnect()
  document.removeEventListener('mousemove', onDragMove)
  document.removeEventListener('mouseup', onDragEnd)
  document.removeEventListener('touchmove', onDragMove)
  document.removeEventListener('touchend', onDragEnd)
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
  { path: '/strategies', label: '策略', icon: 'strategy' },
  { path: '/backtests', label: '回测', icon: 'backtest' },
  { path: '/exchanges', label: '交易所', icon: 'exchange' },
  { path: '/audit-logs', label: '审计日志', icon: 'audit' },
  { path: '/settings', label: '系统设置', icon: 'settings' },
  { path: '/notifications', label: '通知渠道', icon: 'notification' },
  { path: '/users', label: '用户管理', icon: 'users' },
]

const iconMap: Record<string, string> = {
  dashboard: 'icon-dashboard',
  traders: 'icon-user-group',
  backtest: 'icon-common',
  exchange: 'icon-storage',
  strategy: 'icon-common',
  audit: 'icon-file',
  settings: 'icon-settings',
  notification: 'icon-notification',
  users: 'icon-user'
}

const selectedKeys = computed(() => [route.path])

// Path segment → breadcrumb label mapping
const segmentLabel: Record<string, string> = {
  traders: '交易员',
  backtests: '回测',
  strategies: '策略',
  positions: '交易员',
  orders: '交易员'
}

const breadcrumbItems = computed(() => {
  const segments = route.path.split('/').filter(Boolean)
  // Find last non-UUID segment index
  const uuidRe = /^[0-9a-f]{8}(-[0-9a-f]{4}){3}-[0-9a-f]{12}$/
  let lastSegIdx = -1
  for (let i = segments.length - 1; i >= 0; i--) {
    if (!uuidRe.test(segments[i])) { lastSegIdx = i; break }
  }

  const items: { path: string; label: string; isLast: boolean }[] = []
  let accumulated = ''
  for (let i = 0; i < segments.length; i++) {
    const seg = segments[i]
    if (uuidRe.test(seg)) continue
    accumulated = accumulated ? `${accumulated}/${seg}` : seg
    const isLast = i === lastSegIdx
    const label = isLast
      ? (route.meta?.label as string) || segmentLabel[seg] || seg
      : segmentLabel[seg] || seg
    items.push({ path: '/' + accumulated, label, isLast })
  }
  return items
})

const pageTitle = computed(() => {
  const items = breadcrumbItems.value
  return items.length > 0 ? items[items.length - 1].label : '仪表盘'
})
</script>

<template>
  <a-layout class="layout">
    <a-layout-sider
      class="sidebar"
      :width="220"
      theme="light"
    >
      <div class="sider-title" @click="router.push('/')">TradeX</div>
      <a-menu
        :selected-keys="selectedKeys"
        @menu-item-click="(key: string) => router.push(key)"
      >
        <a-menu-item v-for="item in navItems" :key="item.path">
          <component :is="iconMap[item.icon]" />
          {{ item.label }}
        </a-menu-item>
      </a-menu>
    </a-layout-sider>

    <a-layout class="right-layout">
      <a-card class="breadcrumb-card">
        <a-breadcrumb>
          <a-breadcrumb-item>
            <router-link to="/">首页</router-link>
          </a-breadcrumb-item>
          <a-breadcrumb-item v-for="(crumb, i) in breadcrumbItems" :key="i">
            <router-link v-if="!crumb.isLast" :to="crumb.path">{{ crumb.label }}</router-link>
            <span v-else>{{ crumb.label }}</span>
          </a-breadcrumb-item>
        </a-breadcrumb>
      </a-card>

      <a-layout-content class="main">
        <div class="fab-logout" ref="fabEl" title="退出登录"
          @mousedown="onDragStart"
          @touchstart.prevent="onDragStart"
          :class="{ dragging: fabDragging }"
        >
          <a-button shape="circle" type="outline" status="danger" @click="handleFabClick">
            <template #icon><icon-poweroff /></template>
          </a-button>
        </div>
        <a-card class="content-card">
          <template #title>{{ pageTitle }}</template>
          <ErrorBoundary>
            <router-view />
          </ErrorBoundary>
        </a-card>
      </a-layout-content>
    </a-layout>
  </a-layout>
</template>

<style scoped>
.layout {
  min-height: 100vh;
  background: transparent;
}
.sidebar {
  background: rgba(255, 255, 255, 0.82);
  backdrop-filter: blur(8px);
  border-right: 1px solid rgba(0, 0, 0, 0.06);
}
.sider-title {
  padding: 1rem 1rem 0.5rem;
  font-size: 1.25rem;
  font-weight: 700;
  letter-spacing: -0.03em;
  cursor: pointer;
  color: var(--text-primary);
}
.sidebar :deep(.arco-menu) {
  border: none;
  background: transparent;
}
.sidebar :deep(.arco-menu-item) {
  font-size: 0.9rem;
  height: auto;
  line-height: 1.4;
  padding: 0.5rem 0.75rem;
  margin: 0 0.5rem;
  border-radius: 6px;
}
.sidebar :deep(.arco-menu-item svg) {
  width: 1.1rem;
  height: 1.1rem;
}
.sidebar :deep(.arco-menu-selected) {
  font-weight: 600;
}
.fab-logout {
  position: fixed;
  bottom: 1.5rem;
  right: 1.5rem;
  z-index: 1000;
  cursor: grab;
}
.fab-logout:active { cursor: grabbing; }
.fab-logout.dragging { user-select: none; }
.fab-logout.dragging :deep(.arco-btn) { pointer-events: none; }
.fab-logout :deep(.arco-btn) {
  width: 40px;
  height: 40px;
  font-size: 18px;
  background: rgba(255, 255, 255, 0.9);
  backdrop-filter: blur(4px);
  box-shadow: 0 2px 12px rgba(0,0,0,0.1);
}
.fab-logout :deep(.arco-btn:hover) {
  background: #fff;
  box-shadow: 0 4px 16px rgba(0,0,0,0.15);
}
.right-layout {
  flex: 1;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}
.breadcrumb-card {
  margin: 0.75rem 0.75rem 0;
  flex-shrink: 0;
  border-radius: 8px;
}
.breadcrumb-card :deep(.arco-card-body) {
  padding: 0.5rem 1rem;
}
.main {
  flex: 1;
  overflow-y: auto;
  padding: 0.75rem;
  min-width: 0;
}
.content-card {
  min-height: 100%;
  border-radius: 8px;
}
.content-card :deep(.arco-card-body) {
  padding: 1.5rem;
}
</style>
