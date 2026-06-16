<script setup lang="ts">
import { computed } from 'vue'
import { exchangeInfos, getExchangeInfo } from '../api/exchangeInfo'

const props = withDefaults(defineProps<{
  modelValue: string
  disabled?: boolean
}>(), {
  modelValue: '',
  disabled: false
})

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const options = computed(() =>
  exchangeInfos.map(info => ({ label: info.label, value: info.type }))
)

function onSelect(value: string | number | boolean | Record<string, unknown> | (string | number | boolean | Record<string, unknown>)[]) {
  emit('update:modelValue', String(value))
}
</script>

<template>
  <a-select
    :model-value="modelValue"
    :disabled="disabled"
    placeholder="选择交易所类型"
    style="width: 100%"
    @change="onSelect"
  >
    <template #label>
      <span v-if="modelValue" class="exchange-option">
        <span class="exchange-icon"><img :src="getExchangeInfo(modelValue).icon" :alt="getExchangeInfo(modelValue).label" /></span>
        <span class="exchange-label">{{ getExchangeInfo(modelValue).label }}</span>
      </span>
    </template>
    <a-option
      v-for="opt in options"
      :key="opt.value"
      :value="opt.value"
      :label="opt.label"
    >
      <span class="exchange-option">
        <span class="exchange-icon"><img :src="getExchangeInfo(opt.value).icon" :alt="opt.label" /></span>
        <span class="exchange-label">{{ opt.label }}</span>
      </span>
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
.exchange-option {
  display: inline-flex;
  align-items: center;
}
</style>
