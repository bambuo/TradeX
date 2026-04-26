<script setup lang="ts">
import { computed } from 'vue'
import AppSelect from './AppSelect.vue'
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

const options = computed(() =>
  exchangeInfos.map(info => ({ label: info.label, value: info.type }))
)

const selected = computed(() => getExchangeInfo(props.modelValue))

function onSelect(value: string | number) {
  emit('update:modelValue', String(value))
}
</script>

<template>
  <AppSelect
    :options="options"
    :model-value="modelValue"
    :disabled="disabled"
    full
    form
    @update:model-value="onSelect"
  >
    <template #trigger>
      <span class="exchange-icon"><img :src="selected.svgUrl" :alt="selected.label" /></span>
      <span class="exchange-label">{{ selected.label }}</span>
    </template>
    <template #option="{ option }">
      <span class="exchange-icon"><img :src="getExchangeInfo(String(option.value)).svgUrl" :alt="option.label" /></span>
      <span class="exchange-label">{{ option.label }}</span>
    </template>
  </AppSelect>
</template>

<style scoped>
:deep(.app-select-trigger) {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  text-align: left;
}
:deep(.app-select-option) {
  display: flex;
  align-items: center;
  gap: 0.5rem;
}

.exchange-icon {
  width: 20px;
  height: 20px;
  display: inline-flex;
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
</style>
