<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { createChart, ColorType, CandlestickSeries, HistogramSeries, createSeriesMarkers, type IChartApi, type ISeriesApi, type ISeriesMarkersPluginApi, type CandlestickData, type HistogramData, type Time } from 'lightweight-charts'
import type { BacktestKlineAnalysis } from '../api/backtests'
import { formatSmallNumber } from '../utils/format'

const props = defineProps<{
  analysis: BacktestKlineAnalysis[]
  currentIndex?: number
}>()

const chartRef = ref<HTMLDivElement>()
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

function render() {
  if (!chartRef.value || props.analysis.length === 0) return

  if (!chart) {
    const precision = calcPricePrecision(props.analysis.map(a => a.close))

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

    chart.resize(chartRef.value.clientWidth, 360)

    seriesMarkersPlugin = createSeriesMarkers<Time>(candleSeries)
  }

  const candleData: CandlestickData[] = []
  const volumeData: HistogramData[] = []

  for (const a of props.analysis) {
    const time = Math.floor(new Date(a.timestamp).getTime() / 1000) as Time
    candleData.push({ time, open: a.open, high: a.high, low: a.low, close: a.close })
    const isUp = a.close >= a.open
    volumeData.push({
      time,
      value: a.volume,
      color: isUp ? 'rgba(34,197,94,0.4)' : 'rgba(239,68,68,0.4)'
    })
  }

  candleSeries!.setData(candleData)
  volumeSeries!.setData(volumeData)

  chart.timeScale().fitContent()
  updateMarker()
}

function updateMarker() {
  if (!seriesMarkersPlugin) return
  if (props.currentIndex === undefined || props.currentIndex < 0 || props.currentIndex >= props.analysis.length) {
    seriesMarkersPlugin.setMarkers([])
    return
  }
  const item = props.analysis[props.currentIndex]
  const time = Math.floor(new Date(item.timestamp).getTime() / 1000) as Time
  seriesMarkersPlugin.setMarkers([
    { time, position: 'aboveBar', shape: 'arrowDown', color: '#fbbf24', size: 1 }
  ])
}

function handleResize() {
  if (chart && chartRef.value) {
    chart.resize(chartRef.value.clientWidth, 360)
  }
}

watch(() => props.analysis, render, { deep: true })
watch(() => props.currentIndex, updateMarker)
onMounted(() => {
  render()
  window.addEventListener('resize', handleResize)
})
onUnmounted(() => {
  seriesMarkersPlugin?.detach()
  chart?.remove()
  window.removeEventListener('resize', handleResize)
})
</script>

<template>
  <div ref="chartRef" class="kline-chart" />
</template>

<style scoped>
.kline-chart {
  width: 100%;
  height: 360px;
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  margin-top: 0.5rem;
  overflow: hidden;
}
</style>
