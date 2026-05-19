<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { createChart, ColorType, CandlestickSeries, HistogramSeries, createSeriesMarkers, type IChartApi, type ISeriesApi, type ISeriesMarkersPluginApi, type CandlestickData, type HistogramData, type Time } from 'lightweight-charts'
import type { BacktestKlineAnalysis } from '../api/backtests'
import { formatSmallNumber } from '../utils/format'

const props = defineProps<{
  allData: BacktestKlineAnalysis[]
  currentIndex: number
  autoScroll?: boolean
}>()

const chartRef = ref<HTMLDivElement>()
const cursorLineRef = ref<HTMLDivElement>()
let chart: IChartApi | null = null
let candleSeries: ISeriesApi<'Candlestick'> | null = null
let volumeSeries: ISeriesApi<'Histogram'> | null = null
let seriesMarkersPlugin: ISeriesMarkersPluginApi<Time> | null = null
function calcPricePrecision(values: number[]): number {
  let minAbs = Infinity
  for (const v of values) {
    const abs = Math.abs(v)
    if (abs > 0 && abs < minAbs) minAbs = abs
  }
  if (minAbs === Infinity || minAbs >= 1) return 4
  const str = minAbs.toFixed(20)
  const m = str.match(/^0\.(0*)/)
  if (!m) return 6
  const zeroCount = m[1].length
  return Math.min(zeroCount + 4, 12)
}

function initChart() {
  if (!chartRef.value || props.allData.length === 0 || chart) return

  const precision = calcPricePrecision(props.allData.map(a => a.close))

  chart = createChart(chartRef.value, {
    width: chartRef.value.clientWidth,
    height: 360,
    layout: {
      background: { type: ColorType.Solid, color: 'transparent' },
      textColor: '#94a3b8',
      fontSize: 10,
      attributionLogo: false
    },
    grid: {
      vertLines: { visible: false },
      horzLines: { visible: false }
    },
    crosshair: {
      mode: 0,
      vertLine: { color: '#3b4a5e', width: 1, style: 2, labelBackgroundColor: '#334155' },
      horzLine: { color: '#3b4a5e', width: 1, style: 2, labelBackgroundColor: '#334155' }
    },
    rightPriceScale: {
      borderColor: '#334155',
      scaleMargins: { top: 0.05, bottom: 0.25 },
      visible: true
    },
    timeScale: {
      borderColor: '#334155',
      timeVisible: true,
      secondsVisible: false
    },
    handleScroll: { vertTouchDrag: false },
    localization: {
      priceFormatter: (price: number) => formatSmallNumber(price)
    }
  })

  candleSeries = chart.addSeries(CandlestickSeries, {
    upColor: 'var(--accent-green)',
    downColor: '#ef4444',
    borderUpColor: 'var(--accent-green)',
    borderDownColor: '#ef4444',
    wickUpColor: 'var(--accent-green)',
    wickDownColor: '#ef4444',
    lastValueVisible: true,
    priceLineVisible: true,
    priceLineColor: '#94a3b8',
    priceLineWidth: 1,
    priceLineStyle: 2,
    priceLineSource: 0,
    priceFormat: {
      type: 'price',
      precision,
      minMove: Math.pow(10, -precision)
    }
  })

  volumeSeries = chart.addSeries(HistogramSeries, {
    color: 'rgba(148,163,184,0.3)',
    priceFormat: { type: 'volume' },
    priceScaleId: 'volume'
  })

  chart.priceScale('volume').applyOptions({
    scaleMargins: { top: 0.75, bottom: 0 },
    visible: true,
    borderVisible: false
  })

  seriesMarkersPlugin = createSeriesMarkers<Time>(candleSeries)

  const candleData: CandlestickData[] = []
  const volumeData: HistogramData[] = []
  for (const a of props.allData) {
    const time = Math.floor(new Date(a.timestamp).getTime() / 1000) as Time
    candleData.push({ time, open: a.open, high: a.high, low: a.low, close: a.close })
    const isUp = a.close >= a.open
    volumeData.push({
      time,
      value: a.volume,
      color: isUp ? 'rgba(34,197,94,0.4)' : 'rgba(239,68,68,0.4)'
    })
  }

  candleSeries.setData(candleData)
  volumeSeries.setData(volumeData)
  chart.timeScale().fitContent()

  chart.timeScale().subscribeVisibleLogicalRangeChange(() => {
    recalcCursorLine(props.currentIndex)
  })

  chart.resize(chartRef.value.clientWidth, 360)
  moveCursor(props.currentIndex)
}

function recalcCursorLine(index: number) {
  if (!chart || !cursorLineRef.value || props.allData.length === 0) return
  const idx = Math.max(0, Math.min(index, props.allData.length - 1))
  const item = props.allData[idx]
  if (!item) return
  const time = Math.floor(new Date(item.timestamp).getTime() / 1000) as Time
  const x = chart.timeScale().timeToCoordinate(time)
  cursorLineRef.value.style.left = x !== null ? `${x}px` : '0px'
  cursorLineRef.value.style.display = x !== null ? 'block' : 'none'
}

function updateMarker(index: number) {
  if (!seriesMarkersPlugin || props.allData.length === 0) return
  const idx = Math.max(0, Math.min(index, props.allData.length - 1))
  const item = props.allData[idx]
  if (!item) return
  const time = Math.floor(new Date(item.timestamp).getTime() / 1000) as Time
  seriesMarkersPlugin.setMarkers([
    { time, position: 'inBar', shape: 'circle', color: '#fbbf24', size: 2 }
  ])
}

function ensureCursorVisible(index: number) {
  if (!chart) return
  const logicalRange = chart.timeScale().getVisibleLogicalRange()
  if (!logicalRange) return
  const margin = 5
  if (index >= logicalRange.to - margin && index < props.allData.length - 1) {
    chart.timeScale().scrollToPosition(index - logicalRange.to + margin + 1, false)
  }
}

function moveCursor(index: number) {
  updateMarker(index)
  if (props.autoScroll) ensureCursorVisible(index)
  recalcCursorLine(index)
}

function handleResize() {
  if (chart && chartRef.value) {
    chart.resize(chartRef.value.clientWidth, 360)
    recalcCursorLine(props.currentIndex)
  }
}

watch(() => props.allData, () => {
  if (chartRef.value && props.allData.length > 0 && !chart) {
    initChart()
  }
})

watch(() => props.currentIndex, moveCursor)

watch(() => props.autoScroll, () => {
  if (props.autoScroll) ensureCursorVisible(props.currentIndex)
})

onMounted(() => {
  if (props.allData.length > 0 && !chart) initChart()
  window.addEventListener('resize', handleResize)
})

onUnmounted(() => {
  seriesMarkersPlugin?.detach()
  chart?.remove()
  chart = null
  candleSeries = null
  volumeSeries = null
  seriesMarkersPlugin = null
  window.removeEventListener('resize', handleResize)
})
</script>

<template>
  <div ref="chartRef" class="chart-wrapper">
    <div ref="cursorLineRef" class="cursor-line" />
  </div>
</template>

<style scoped>
.chart-wrapper {
  position: relative;
  width: 100%;
  height: 360px;
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  margin-top: 0.5rem;
  overflow: hidden;
}
.cursor-line {
  display: none;
  position: absolute;
  top: 0;
  bottom: 26px;
  width: 2px;
  background: #fbbf24;
  box-shadow: 0 0 6px rgba(251, 191, 36, 0.5);
  pointer-events: none;
  z-index: 10;
  transition: left 0.05s ease;
}
</style>
