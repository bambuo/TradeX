<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import * as echarts from 'echarts'

const props = defineProps<{
  trades: Array<{ entryTime: string; pnlPercent: number }>
}>()

const chartRef = ref<HTMLDivElement>()
let chart: echarts.ECharts | null = null

function render() {
  if (!chartRef.value || !props.trades?.length) return

  if (!chart) {
    chart = echarts.init(chartRef.value)
  }

  const data = props.trades.map((t, i) => ({
    index: i + 1,
    time: new Date(t.entryTime).toLocaleDateString(),
    pnl: t.pnlPercent
  }))

  let cumulative = 0
  const cumulativePnl = data.map(d => {
    cumulative += d.pnl
    return cumulative
  })

  const peak = [...cumulativePnl]
  const drawdown = cumulativePnl.map((v, i) => {
    const maxPeak = Math.max(...peak.slice(0, i + 1))
    return maxPeak > 0 ? ((v - maxPeak) / maxPeak * 100) : 0
  })

  chart.setOption({
    tooltip: { trigger: 'axis' },
    legend: { data: ['累计收益率 %', '回撤 %'], textStyle: { color: '#94a3b8' } },
    grid: { left: 60, right: 20, top: 40, bottom: 30 },
    xAxis: {
      type: 'category',
      data: data.map(d => d.time),
      axisLabel: { color: '#64748b', fontSize: 10 }
    },
    yAxis: [
      {
        type: 'value',
        name: '收益率 %',
        axisLabel: { color: '#94a3b8' },
        splitLine: { lineStyle: { color: '#1e293b' } }
      },
      {
        type: 'value',
        name: '回撤 %',
        axisLabel: { color: '#94a3b8' },
        splitLine: { show: false }
      }
    ],
    series: [
      {
        name: '累计收益率 %',
        type: 'line',
        data: cumulativePnl.map(v => Math.round(v * 100) / 100),
        smooth: true,
        lineStyle: { color: 'var(--accent-green)', width: 2 },
        areaStyle: { color: 'rgba(34,197,94,0.1)' },
        symbol: 'none'
      },
      {
        name: '回撤 %',
        type: 'line',
        yAxisIndex: 1,
        data: drawdown.map(v => Math.round(v * 100) / 100),
        smooth: true,
        lineStyle: { color: '#ef4444', width: 2 },
        areaStyle: { color: 'rgba(239,68,68,0.1)' },
        symbol: 'none'
      }
    ]
  })
}

watch(() => props.trades, render, { deep: true })
onMounted(render)

onUnmounted(() => {
  chart?.dispose()
})
</script>

<template>
  <div ref="chartRef" class="chart-container" />
</template>

<style scoped>
.chart-container {
  width: 100%;
  height: 300px;
  background: rgba(255,255,255,0.55);
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  margin-bottom: 1rem;
}
</style>
