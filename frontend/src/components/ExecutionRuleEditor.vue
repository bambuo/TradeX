<script setup lang="ts">
import { computed } from 'vue'

export interface ExecutionRule {
  type: string
  gridLevels?: number
  gridSpacingPercent?: number
  basePositionSize?: number
  maxPositionSize?: number
  maxDailyLoss?: number
  slippageTolerance?: number
  trailingStopPercent?: number
  takeProfitPercent?: number
  usePyramiding?: boolean
  maxPyramidingLevels?: number
}

const props = defineProps<{
  modelValue: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const rule = computed({
  get: () => {
    try { return JSON.parse(props.modelValue) as ExecutionRule }
    catch { return { type: 'custom' } as ExecutionRule }
  },
  set: (val: ExecutionRule) => emit('update:modelValue', JSON.stringify(val, null, 2))
})

function setField(field: string, value: any) {
  rule.value = { ...rule.value, [field]: value }
}

const ruleTypes: Record<string, string> = {
  custom: '自定义',
  grid: '网格策略',
  trend_following: '趋势追踪',
  infinity_grid: '无限网格'
}

const ruleFields: Record<string, { key: string; label: string; type: string; step?: string; min?: number }[]> = {
  custom: [],
  grid: [
    { key: 'gridLevels', label: '网格层数', type: 'number', min: 1 },
    { key: 'gridSpacingPercent', label: '网格间距 (%)', type: 'number', step: '0.1', min: 0 },
    { key: 'basePositionSize', label: '基础仓位 ($)', type: 'number', min: 1 },
    { key: 'maxPositionSize', label: '最大仓位 ($)', type: 'number', min: 1 },
    { key: 'maxDailyLoss', label: '每日最大亏损 ($)', type: 'number', min: 0 },
    { key: 'slippageTolerance', label: '滑点容忍度', type: 'number', step: '0.0001', min: 0 }
  ],
  trend_following: [
    { key: 'trailingStopPercent', label: '追踪止损 (%)', type: 'number', step: '0.1', min: 0 },
    { key: 'takeProfitPercent', label: '止盈 (%)', type: 'number', step: '0.1', min: 0 },
    { key: 'basePositionSize', label: '基础仓位 ($)', type: 'number', min: 1 },
    { key: 'maxPositionSize', label: '最大仓位 ($)', type: 'number', min: 1 },
    { key: 'maxDailyLoss', label: '每日最大亏损 ($)', type: 'number', min: 0 },
    { key: 'slippageTolerance', label: '滑点容忍度', type: 'number', step: '0.0001', min: 0 }
  ],
  infinity_grid: [
    { key: 'gridLevels', label: '网格层数', type: 'number', min: 1 },
    { key: 'gridSpacingPercent', label: '网格间距 (%)', type: 'number', step: '0.1', min: 0 },
    { key: 'basePositionSize', label: '基础仓位 ($)', type: 'number', min: 1 },
    { key: 'maxPositionSize', label: '最大仓位 ($)', type: 'number', min: 1 },
    { key: 'maxDailyLoss', label: '每日最大亏损 ($)', type: 'number', min: 0 },
    { key: 'slippageTolerance', label: '滑点容忍度', type: 'number', step: '0.0001', min: 0 },
    { key: 'usePyramiding', label: '启用金字塔加仓', type: 'boolean' },
    { key: 'maxPyramidingLevels', label: '最大金字塔层数', type: 'number', min: 1 }
  ]
}

const currentFields = computed(() => ruleFields[rule.value.type] || ruleFields.custom)
</script>

<template>
  <div class="rule-editor">
    <div class="rule-type-row">
      <label class="rule-label">执行规则类型</label>
      <select
        :value="rule.type"
        class="rule-select"
        @change="(e) => setField('type', (e.target as HTMLSelectElement).value)"
      >
        <option v-for="(label, key) in ruleTypes" :key="key" :value="key">{{ label }}</option>
      </select>
    </div>

    <div v-if="currentFields.length" class="fields-grid">
      <div v-for="field in currentFields" :key="field.key" class="field-item">
        <label class="field-label">{{ field.label }}</label>
        <input
          v-if="field.type === 'number'"
          :value="(rule as any)[field.key] ?? ''"
          :step="field.step"
          :min="field.min"
          type="number"
          class="field-input"
          @input="(e) => setField(field.key, parseFloat((e.target as HTMLInputElement).value) || 0)"
        />
        <label v-else-if="field.type === 'boolean'" class="field-checkbox">
          <input
            :checked="!!(rule as any)[field.key]"
            type="checkbox"
            @change="(e) => setField(field.key, (e.target as HTMLInputElement).checked)"
          />
          {{ (rule as any)[field.key] ? '是' : '否' }}
        </label>
      </div>
    </div>

    <details class="raw-toggle">
      <summary>查看/编辑原始 JSON</summary>
      <textarea
        :value="props.modelValue"
        class="raw-input"
        rows="4"
        @input="(e) => emit('update:modelValue', (e.target as HTMLTextAreaElement).value)"
      ></textarea>
    </details>
  </div>
</template>

<style scoped>
.rule-editor {
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  padding: 0.75rem;
  background: rgba(255,255,255,0.55);
}
.rule-type-row {
  margin-bottom: 0.75rem;
}
.rule-label {
  display: block;
  color: var(--text-muted);
  font-size: 0.85rem;
  margin-bottom: 0.25rem;
}
.rule-select {
  width: 100%;
  padding: 0.5rem 0.625rem;
  background: rgba(255,255,255,0.35);
  color: var(--text-primary);
  border: 1px solid var(--glass-border-strong);
  border-radius: 4px;
  font-size: 0.85rem;
}
.fields-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.5rem;
}
.field-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}
.field-label {
  color: var(--text-muted);
  font-size: 0.8rem;
}
.field-input {
  width: 100%;
  padding: 0.5rem 0.625rem;
  background: rgba(255,255,255,0.35);
  color: var(--text-primary);
  border: 1px solid var(--glass-border-strong);
  border-radius: 4px;
  font-size: 0.85rem;
  box-sizing: border-box;
}
.field-checkbox {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  color: var(--text-primary);
  font-size: 0.85rem;
  cursor: pointer;
  padding-top: 0.375rem;
}
.raw-toggle {
  margin-top: 0.75rem;
  color: var(--text-muted);
  font-size: 0.8rem;
}
.raw-toggle summary { cursor: pointer; }
.raw-input {
  width: 100%;
  margin-top: 0.5rem;
  padding: 0.5rem;
  background: rgba(255,255,255,0.35);
  color: var(--text-primary);
  border: 1px solid var(--glass-border-strong);
  border-radius: 4px;
  font-family: monospace;
  font-size: 0.8rem;
  box-sizing: border-box;
}
</style>
