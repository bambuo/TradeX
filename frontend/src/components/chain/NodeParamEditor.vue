<script setup lang="ts">
import { computed } from 'vue'
import type { ParamDescriptor } from '../../api/strategies'
import type { NodeInstance } from './types'

const props = defineProps<{
  params: Record<string, unknown>
  descriptor: ParamDescriptor
  upstreamEmitNames: string[]
}>()

const emit = defineEmits<{
  update: [value: unknown]
}>()

const model = computed({
  get: () => {
    const val = props.params[props.descriptor.name]
    if (val !== undefined) return val
    return props.descriptor.default ?? (props.descriptor.type === 'bool' ? false : '')
  },
  set: (val: unknown) => emit('update', val)
})

const hasError = computed(() => {
  if (props.descriptor.required) {
    const val = model.value
    if (val === '' || val === undefined || val === null) return true
  }
  const num = Number(model.value)
  if (props.descriptor.type === 'int' || props.descriptor.type === 'float') {
    if (props.descriptor.min != null && num < props.descriptor.min) return true
    if (props.descriptor.max != null && num > props.descriptor.max) return true
  }
  return false
})

const enumOptions = computed(() => {
  if (props.descriptor.enum) return props.descriptor.enum
  return []
})
</script>

<template>
  <div class="param-field" :class="{ 'has-error': hasError }">
    <label class="param-label">
      {{ descriptor.name }}
      <span v-if="descriptor.required" class="required-mark">*</span>
      <span v-if="descriptor.unit" class="param-unit">({{ descriptor.unit }})</span>
    </label>

    <template v-if="descriptor.type === 'bool'">
      <a-switch
        :model-value="!!model"
        @update:model-value="emit('update', $event)"
      />
    </template>

    <template v-else-if="descriptor.type === 'int' || descriptor.type === 'float'">
      <a-input-number
        :model-value="Number(model) || 0"
        :min="descriptor.min"
        :max="descriptor.max"
        :precision="descriptor.type === 'float' ? 4 : 0"
        :step="descriptor.type === 'float' ? 0.01 : 1"
        hide-button
        size="mini"
        @update:model-value="emit('update', $event)"
      />
      <div v-if="hasError" class="error-text">
        {{ descriptor.min != null && Number(model) < descriptor.min
          ? `最小值 ${descriptor.min}`
          : descriptor.max != null && Number(model) > descriptor.max
            ? `最大值 ${descriptor.max}`
            : '必填'
        }}
      </div>
    </template>

    <template v-else-if="descriptor.type === 'string[]'">
      <a-select
        :model-value="(model as string[]) || []"
        multiple
        :placeholder="descriptor.description"
        size="mini"
        @update:model-value="emit('update', $event)"
      >
        <a-option
          v-for="opt in enumOptions"
          :key="opt"
          :value="opt"
          :label="opt"
        />
      </a-select>
    </template>

    <template v-else-if="descriptor.type === 'ref'">
      <a-select
        :model-value="model as string"
        :placeholder="'选择 ' + descriptor.description"
        allow-clear
        size="mini"
        @update:model-value="emit('update', $event || '')"
      >
        <a-option
          v-for="name in upstreamEmitNames"
          :key="name"
          :value="name"
          :label="name"
        />
      </a-select>
    </template>

    <template v-else>
      <!-- string / fallback -->
      <a-input
        :model-value="model as string"
        :placeholder="descriptor.description"
        size="mini"
        @update:model-value="emit('update', $event)"
      />
    </template>

    <div v-if="!hasError && descriptor.description" class="hint-text">
      {{ descriptor.description }}
    </div>
  </div>
</template>

<style scoped>
.param-field {
  display: flex;
  flex-direction: column;
  gap: 3px;
  padding: 4px 0;
}
.param-field.has-error .param-label { color: rgb(var(--red-6)); }
.param-label {
  font-size: 11px;
  font-weight: 500;
  color: var(--color-text-2);
}
.required-mark { color: rgb(var(--red-6)); margin-left: 2px; }
.param-unit { color: var(--color-text-4); font-weight: 400; margin-left: 2px; }
.error-text { font-size: 10px; color: rgb(var(--red-6)); }
.hint-text { font-size: 10px; color: var(--color-text-4); }
</style>
