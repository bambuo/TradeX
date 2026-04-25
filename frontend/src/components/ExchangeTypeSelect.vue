<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { exchangeInfos, getExchangeInfo } from '../api/exchangeInfo'

const props = withDefaults(defineProps<{
  modelValue: string
  disabled?: boolean
}>(), {
  disabled: false
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const open = ref(false)
const triggerRef = ref<HTMLElement | null>(null)

const selected = computed(() => getExchangeInfo(props.modelValue))

function select(type: string) {
  emit('update:modelValue', type)
  open.value = false
}

function toggle() {
  if (!props.disabled) open.value = !open.value
}

function onClickOutside(e: MouseEvent) {
  if (triggerRef.value && !triggerRef.value.contains(e.target as Node)) {
    open.value = false
  }
}

onMounted(() => document.addEventListener('click', onClickOutside))
onUnmounted(() => document.removeEventListener('click', onClickOutside))
</script>

<template>
  <div ref="triggerRef" class="exchange-select" :class="{ disabled }">
    <div class="exchange-select-trigger" @click="toggle">
      <span class="exchange-icon"><img :src="selected.svgUrl" :alt="selected.label" /></span>
      <span class="exchange-label">{{ selected.label }}</span>
      <span class="exchange-arrow">▾</span>
    </div>
    <div v-if="open" class="exchange-dropdown">
      <div
        v-for="info in exchangeInfos"
        :key="info.type"
        class="exchange-option"
        :class="{ active: info.type === modelValue }"
        @click="select(info.type)"
      >
        <span class="exchange-icon"><img :src="info.svgUrl" :alt="info.label" /></span>
        <span class="exchange-label">{{ info.label }}</span>
      </div>
    </div>
  </div>
</template>

<style scoped>
.exchange-select {
  position: relative;
  width: 100%;
  margin-bottom: 1rem;
}
.exchange-select.disabled {
  opacity: 0.5;
  pointer-events: none;
}
.exchange-select-trigger {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.75rem;
  border: 1px solid #334155;
  border-radius: 4px;
  background: #0f172a;
  color: #e2e8f0;
  cursor: pointer;
  box-sizing: border-box;
}
.exchange-select-trigger:hover {
  border-color: #475569;
}
.exchange-icon {
  width: 20px;
  height: 20px;
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}
.exchange-icon img {
  width: 20px;
  height: 20px;
}
.exchange-label {
  flex: 1;
  font-size: 0.9rem;
}
.exchange-arrow {
  color: #64748b;
  font-size: 0.75rem;
}
.exchange-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  right: 0;
  z-index: 10;
  background: #1e293b;
  border: 1px solid #334155;
  border-radius: 6px;
  overflow: hidden;
}
.exchange-option {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.625rem 0.75rem;
  cursor: pointer;
  color: #e2e8f0;
  font-size: 0.9rem;
}
.exchange-option:hover {
  background: #334155;
}
.exchange-option.active {
  background: rgba(56, 189, 248, 0.1);
  color: #38bdf8;
}
</style>
