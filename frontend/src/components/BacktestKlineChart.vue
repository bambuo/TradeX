<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted, computed } from 'vue'
import * as echarts from 'echarts'
import type { BacktestCandleAnalysis } from '../api/backtests'

const props = defineProps<{
  analysis: BacktestCandleAnalysis[]
}>()

const chartRef = ref<HTMLDivElement>()
let chart: echarts.ECharts | null = null

const entryPoints = computed(() =>
  props.analysis.filter(a => a.action === 'enter')
)
const exitPoints = computed(() =>
  props.analysis.filter(a => a.action === 'exit')
)

function render() {
  if (!chartRef.value || props.analysis.length === 0) return

  if (!chart) {
    chart = echarts.init(chartRef.value)
  }

  const dates = props.analysis.map(a => new Date(a.timestamp).toLocaleString())
  const ohlc = props.analysis.map(a => [a.open, a.close, a.low, a.high])
  const volumes = props.analysis.map(a => a.volume)

  const entryData = entryPoints.value.map(a => ({
    value: [a.index, a.high],
    itemStyle: { color: '#22c55e' }
  }))
  const exitData = exitPoints.value.map(a => ({
    value: [a.index, a.low],
    itemStyle: { color: '#ef4444' }
  }))

  chart.setOption({
    animation: false,
    tooltip: {
      trigger: 'axis',
      axisPointer: { type: 'cross' },
      backgroundColor: '#1e293b',
      borderColor: '#334155',
      textStyle: { color: '#e2e8f0', fontSize: 11 }
    },
    axisPointer: {
      link: [{ xAxisIndex: 'all' }],
      label: { backgroundColor: '#334155' }
    },
    grid: [
      { left: 55, right: 55, top: 10, height: '55%' },
      { left: 55, right: 55, top: '73%', height: '12%' }
    ],
    xAxis: [
      {
        type: 'category',
        data: dates,
        axisLine: { lineStyle: { color: '#334155' } },
        axisLabel: { show: false },
        splitLine: { show: false },
        min: 0,
        max: dates.length - 1
      },
      {
        type: 'category',
        gridIndex: 1,
        data: dates,
        axisLabel: { show: false },
        splitLine: { show: false }
      }
    ],
    yAxis: [
      {
        type: 'value',
        scale: true,
        splitLine: { lineStyle: { color: '#1e293b' } },
        axisLabel: { color: '#94a3b8', fontSize: 10 }
      },
      {
        type: 'value',
        gridIndex: 1,
        splitNumber: 2,
        splitLine: { show: false },
        axisLabel: { show: false }
      }
    ],
    dataZoom: [
      {
        type: 'inside',
        xAxisIndex: [0, 1],
        start: 0,
        end: 100
      },
      {
        show: true,
        xAxisIndex: [0, 1],
        type: 'slider',
        top: '92%',
        height: 15,
        borderColor: '#334155',
        backgroundColor: '#0f172a',
        fillerColor: 'rgba(56,189,248,0.15)',
        handleStyle: { color: '#38bdf8' },
        textStyle: { color: '#64748b', fontSize: 10 },
        labelPrecision: 0,
        start: 0,
        end: 100
      }
    ],
    series: [
      {
        name: 'K 线',
        type: 'candlestick',
        data: ohlc,
        itemStyle: {
          color: '#22c55e',
          color0: '#ef4444',
          borderColor: '#22c55e',
          borderColor0: '#ef4444'
        }
      },
      {
        name: '入场',
        type: 'scatter',
        data: entryData,
        symbol: 'pin',
        symbolSize: 24,
        itemStyle: { color: '#22c55e' },
        label: {
          show: true,
          formatter: '入场',
          color: '#22c55e',
          fontSize: 10,
          fontWeight: 'bold',
          position: 'top'
        }
      },
      {
        name: '出场',
        type: 'scatter',
        data: exitData,
        symbol: 'pin',
        symbolSize: 24,
        itemStyle: { color: '#ef4444' },
        label: {
          show: true,
          formatter: '出场',
          color: '#ef4444',
          fontSize: 10,
          fontWeight: 'bold',
          position: 'bottom'
        }
      },
      {
        name: '成交量',
        type: 'bar',
        xAxisIndex: 1,
        yAxisIndex: 1,
        data: volumes.map((v, i) => {
          const isUp = props.analysis[i].close >= props.analysis[i].open
          return {
            value: v,
            itemStyle: { color: isUp ? 'rgba(34,197,94,0.4)' : 'rgba(239,68,68,0.4)' }
          }
        })
      }
    ]
  })
}

function handleResize() {
  chart?.resize()
}

watch(() => props.analysis, render, { deep: true })
onMounted(() => {
  render()
  window.addEventListener('resize', handleResize)
})
onUnmounted(() => {
  chart?.dispose()
  window.removeEventListener('resize', handleResize)
})
</script>

<template>
  <div ref="chartRef" class="kline-chart" />
</template>

<style scoped>
.kline-chart {
  width: 100%;
  height: 520px;
  background: transparent;
  border: 1px solid #334155;
  border-radius: 6px;
  margin-top: 0.5rem;
}
</style>
