<script setup lang="ts">
import { ref, onMounted, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { positionsApi, type Position } from '../api/positions'
import { formatSmallNumber } from '../utils/format'

const route = useRoute()
const router = useRouter()
const traderId = route.params.traderId as string

const positions = ref<Position[]>([])
const loading = ref(true)
const openOnly = ref(false)

async function load() {
  loading.value = true
  try {
    const { data } = await positionsApi.getAll(traderId, openOnly.value)
    positions.value = data
  } finally {
    loading.value = false
  }
}

watch(openOnly, load)
onMounted(load)

function pnlClass(pnl: number): string {
  if (pnl > 0) return 'pnl-positive'
  if (pnl < 0) return 'pnl-negative'
  return ''
}

function formatPnl(pnl: number): string {
  return (pnl >= 0 ? '+' : '') + pnl.toFixed(2)
}
</script>

<template>
  <div class="positions-page">
    <header class="page-header">
      <div class="header-left">
        <a-button type="text" size="small" @click="router.push(`/traders/${traderId}/strategies`)">
          <template #icon><icon-left /></template>
          策略
        </a-button>
        <h2>持仓管理</h2>
      </div>
      <label class="toggle-label">
        <input v-model="openOnly" type="checkbox" />
        仅显示持仓中
      </label>
    </header>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th>交易对</th>
          <th>数量</th>
          <th>入场价</th>
          <th>当前价</th>
          <th>未实现盈亏</th>
          <th>已实现盈亏</th>
          <th>状态</th>
          <th>开仓时间</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="p in positions" :key="p.id">
          <td>{{ p.symbolId }}</td>
          <td>{{ p.quantity }}</td>
          <td>{{ formatSmallNumber(p.entryPrice) }}</td>
          <td>{{ formatSmallNumber(p.currentPrice) }}</td>
          <td :class="pnlClass(p.unrealizedPnl)">{{ formatPnl(p.unrealizedPnl) }}</td>
          <td :class="pnlClass(p.realizedPnl)">{{ formatPnl(p.realizedPnl) }}</td>
          <td>
            <a-tag :color="p.status === 'Open' ? 'blue' : ''">{{ p.status === 'Open' ? '持仓中' : '已平仓' }}</a-tag>
          </td>
          <td>{{ new Date(p.openedAtUtc).toLocaleString() }}</td>
        </tr>
        <tr v-if="positions.length === 0">
          <td colspan="8" class="empty">暂无持仓记录</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.positions-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.header-left { display: flex; align-items: center; gap: 1rem; }
.header-left h2 { margin: 0; color: var(--text-primary); }
.btn-back { background: none; border: 1px solid var(--glass-border-strong); color: var(--text-muted); padding: 0.25rem 0.75rem; border-radius: 4px; cursor: pointer; font-size: 0.9rem; }
.toggle-label { display: flex; align-items: center; gap: 0.5rem; color: var(--text-primary); cursor: pointer; font-size: 0.9rem; }
.toggle-label input { width: 1rem; height: 1rem; cursor: pointer; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid var(--glass-border); color: var(--text-primary); }
.table th { color: var(--text-muted); font-weight: 600; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.pnl-positive { color: var(--accent-green); font-weight: 600; }
.pnl-negative { color: var(--accent-red); font-weight: 600; }
</style>
