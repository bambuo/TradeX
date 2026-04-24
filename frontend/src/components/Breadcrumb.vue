<script setup lang="ts">
import { computed } from 'vue'
import { useRoute } from 'vue-router'

const route = useRoute()

const breadcrumbs = computed(() => {
  const pathParts = route.path.split('/').filter(Boolean)
  const crumbs: { label: string; path: string }[] = []
  let currentPath = ''

  for (const part of pathParts) {
    currentPath += `/${part}`
    const label = route.matched.find(m => m.path === `/${pathParts.slice(0, pathParts.indexOf(part) + 1).join('/')}` || m.path.includes(part))?.meta?.title as string ?? labelFromPath(part)
    crumbs.push({ label, path: currentPath })
  }

  return crumbs
})

const labels: Record<string, string> = {
  traders: '交易员',
  exchanges: '交易所账户',
  strategies: '策略',
  positions: '持仓',
  orders: '订单',
  backtest: '回测',
  mfa: '安全设置',
  setup: 'MFA 设置'
}

function labelFromPath(part: string): string {
  return labels[part.toLowerCase()] ?? part
}
</script>

<template>
  <nav v-if="breadcrumbs.length > 1" class="breadcrumb">
    <router-link v-for="(crumb, i) in breadcrumbs" :key="crumb.path" :to="crumb.path" class="crumb">
      <span v-if="i > 0" class="separator">/</span>
      <span :class="{ current: i === breadcrumbs.length - 1 }">{{ crumb.label }}</span>
    </router-link>
  </nav>
</template>

<style scoped>
.breadcrumb {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  padding: 0.75rem 2rem 0;
  font-size: 0.8rem;
  color: #64748b;
}
.crumb {
  display: flex;
  align-items: center;
  gap: 0.25rem;
  color: #64748b;
  text-decoration: none;
}
.crumb:hover { color: #94a3b8; }
.separator { color: #475569; margin: 0 0.15rem; }
.current { color: #e2e8f0; font-weight: 600; }
</style>
