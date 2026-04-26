<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  name: string
  size?: number
}>(), {
  size: 16
})

const iconPaths: Record<string, string[]> = {
  plus: ['M12 5v14', 'M5 12h14'],
  check: ['M5 13l4 4 10-10'],
  edit: ['M4 20h4l10.5-10.5a2.1 2.1 0 0 0-3-3L5 17v3Z', 'M13.5 7.5l3 3'],
  trash: ['M4 7h16', 'M10 11v6', 'M14 11v6', 'M6 7l1 13h10l1-13', 'M9 7V4h6v3'],
  save: ['M5 4h12l2 2v14H5V4Z', 'M8 4v6h8V4', 'M8 20v-6h8v6'],
  close: ['M6 6l12 12', 'M18 6L6 18'],
  back: ['M15 6l-6 6 6 6', 'M9 12h11'],
  filter: ['M4 6h16', 'M7 12h10', 'M10 18h4'],
  refresh: ['M20 12a8 8 0 0 1-13.5 5.8', 'M4 12A8 8 0 0 1 17.5 6.2', 'M17 3v4h-4', 'M7 21v-4h4'],
  play: ['M8 5v14l11-7-11-7Z'],
  pause: ['M8 5v14', 'M16 5v14'],
  chart: ['M4 19V5', 'M4 19h16', 'M8 15l3-4 3 2 4-7'],
  table: ['M4 5h16v14H4V5Z', 'M4 10h16', 'M4 15h16', 'M10 5v14'],
  test: ['M9 12l2 2 4-5', 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Z'],
  power: ['M12 3v8', 'M7 6.8a8 8 0 1 0 10 0'],
  strategy: ['M5 6h5', 'M14 6h5', 'M5 12h8', 'M17 12h2', 'M5 18h2', 'M11 18h8'],
  positions: ['M4 17l5-5 4 3 7-8', 'M4 20h16'],
  orders: ['M7 4h10', 'M7 9h10', 'M7 14h6', 'M5 20h14a2 2 0 0 0 2-2V4H3v14a2 2 0 0 0 2 2Z'],
  user: ['M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z', 'M4 20a8 8 0 0 1 16 0'],
  exchange: ['M4 7h13l-3-3', 'M17 7l-3 3', 'M20 17H7l3 3', 'M7 17l3-3'],
  bell: ['M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9Z', 'M10 20a2 2 0 0 0 4 0'],
  home: ['M4 11l8-7 8 7', 'M6 10v10h12V10'],
  login: ['M10 17l5-5-5-5', 'M15 12H3', 'M14 4h5v16h-5'],
  key: ['M14 7a5 5 0 1 0-4 4l-6 6v3h3l1-1h2v-2h2l2-2'],
  shield: ['M12 3l7 3v5c0 4.5-2.8 8.5-7 10-4.2-1.5-7-5.5-7-10V6l7-3Z'],
  badge: ['M333.2 278.1c1.6 64 51.3 70.3 51.3 70.3h259.8c42.4 0 49.9-53.3 51.2-72h-2.7l3-0.1c0-92.6-84.9-78.4-84.9-78.4 0-93.9-97.7-90-97.7-90-104.2 0-101.6 90-101.6 90-83.6 0-79.7 78.4-79.7 78.4l1.3 1.8z m179.9-113.6c27.7 0 50.2 22.4 50.2 50.2 0 27.7-22.5 50.1-50.2 50.1-27.7 0-50.2-22.4-50.2-50.1 0-27.7 22.5-50.2 50.2-50.2z m398.5 112c26.7 0 48.4 21.7 48.4 48.4v542.8c0 26.7-21.7 48.4-48.4 48.4H112.4c-26.7 0-48.4-21.7-48.4-48.4V324.9c0-26.7 21.7-48.4 48.4-48.4h178.9c-1.8 23.4-3.3 118.3 88 118.3h275.2s91.1 6.2 81.1-118.3h176z m-319 251c-16 0-28.9 7.5-28.9 23.5s12.9 23.5 28.9 23.5h250.3c16 0 28.9-7.5 28.9-23.5s-13-23.5-28.9-23.5H592.6z m-257.7-58.2c-43 0-77.8 34.8-77.8 77.8s34.8 77.8 77.8 77.8 77.8-34.8 77.8-77.8c0-42.9-34.8-77.8-77.8-77.8z m153.7 298.4v-82.3c0-55.3-91.3-63-91.3-63l-60.5 60.5-73.3-63c-82.3 19.3-81 54-81 54v93.9h306.1v-0.1z m220-8c14.9 0 27-6.6 27-21.5s-12.1-21.5-27-21.5H589.4c-14.9 0-27 6.6-27 21.5s12.1 21.5 27 21.5h119.2z m137.6-91.4c15.6 0 28.3-7.2 28.3-22.8s-12.7-22.8-28.3-22.8H590.7c-15.6 0-28.3 7.2-28.3 22.8s12.7 22.8 28.3 22.8h255.5z']
}

const iconViewBox: Record<string, string> = {
  badge: '0 0 1024 1024'
}

const iconFilled: Record<string, boolean> = {
  badge: true
}

const paths = computed(() => iconPaths[props.name] ?? iconPaths.plus)
const vb = computed(() => iconViewBox[props.name] ?? '0 0 24 24')
const isFilled = computed(() => iconFilled[props.name] ?? false)
</script>

<template>
  <svg
    class="app-icon"
    :width="size"
    :height="size"
    :viewBox="vb"
    :fill="isFilled ? 'currentColor' : 'none'"
    :stroke="isFilled ? 'none' : 'currentColor'"
    :stroke-width="isFilled ? 0 : 1.9"
    stroke-linecap="round"
    stroke-linejoin="round"
    aria-hidden="true"
  >
    <path v-for="path in paths" :key="path" :d="path" />
  </svg>
</template>

<style scoped>
.app-icon {
  display: inline-block;
  flex-shrink: 0;
}
</style>
