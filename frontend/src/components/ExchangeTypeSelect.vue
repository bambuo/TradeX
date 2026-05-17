<script setup lang="ts">
import { computed } from 'vue'
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

function onSelect(value: string | number | boolean | Record<string, any> | (string | number | boolean | Record<string, any>)[]) {
  emit('update:modelValue', String(value))
}
</script>

<template>
  <a-select
    :model-value="modelValue"
    :disabled="disabled"
    style="width: 100%"
    @change="onSelect"
  >
    <template #label="{ data }">
      <span class="exchange-icon"><img :src="getExchangeInfo(String(data?.value ?? modelValue)).icon" alt="" /></span>
      <span class="exchange-label">{{ data?.label ?? getExchangeInfo(modelValue).label }}</span>
    </template>
    <a-option
      v-for="opt in options"
      :key="opt.value"
      :value="opt.value"
      :label="opt.label"
    >
      <span class="exchange-icon"><img :src="getExchangeInfo(opt.value).icon" :alt="opt.label" /></span>
      <span class="exchange-label">{{ opt.label }}</span>
    </a-option>
  </a-select>
</template>

<style scoped>
.exchange-icon {
  width: 20px;
  height: 20px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  margin-right: 6px;
}
.exchange-icon img {
  width: 20px;
  height: 20px;
}
.exchange-label {
  font-size: 0.9rem;
}
</style>
