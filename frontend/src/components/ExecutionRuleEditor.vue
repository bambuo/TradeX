<script setup lang="ts">
import { computed, ref, watch } from 'vue'

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
  entryVolatilityPercent?: number
  rebalancePercent?: number
  noStopLoss?: boolean
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
  infinity_grid: '无限网格',
  volatility_grid: '波幅均价再平衡'
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
  ],
  volatility_grid: [
    { key: 'entryVolatilityPercent', label: '首单波幅阈值 (%)', type: 'number', step: '0.1', min: 0.1 },
    { key: 'rebalancePercent', label: '均价再平衡阈值 (%)', type: 'number', step: '0.1', min: 0.1 },
    { key: 'basePositionSize', label: '基础仓位 ($)', type: 'number', min: 1 },
    { key: 'maxPositionSize', label: '最大仓位 ($)', type: 'number', min: 1 },
    { key: 'maxPyramidingLevels', label: '最大追加次数', type: 'number', min: 0 },
    { key: 'slippageTolerance', label: '滑点容忍度', type: 'number', step: '0.0001', min: 0 },
    { key: 'maxDailyLoss', label: '每日最大亏损 ($)', type: 'number', min: 0 },
    { key: 'noStopLoss', label: '关闭单笔止损', type: 'boolean' }
  ]
}

const currentFields = computed(() => ruleFields[rule.value.type] || ruleFields.custom)

const rawText = ref('')
watch(() => props.modelValue, (v) => { rawText.value = formatJson(v) }, { immediate: true })

function formatJson(json: string): string {
  try { return JSON.stringify(JSON.parse(json), null, 2) }
  catch { return json }
}

function onRawInput(e: Event) {
  rawText.value = (e.target as HTMLTextAreaElement).value
  emit('update:modelValue', (e.target as HTMLTextAreaElement).value)
}
</script>

<template>
  <div class="rule-editor">
    <div class="rule-type-row">
      <label class="rule-label">执行规则类型</label>
      <a-select
        :model-value="rule.type"
        style="width: 100%"
        @change="(v: any) => setField('type', String(v))"
      >
        <a-option v-for="[value, label] in Object.entries(ruleTypes)" :key="value" :value="value" :label="label" />
      </a-select>
    </div>

    <div v-if="currentFields.length" class="fields-grid">
      <div v-for="field in currentFields" :key="field.key" class="field-item">
        <label class="field-label">{{ field.label }}</label>
        <a-input-number
          v-if="field.type === 'number'"
          :model-value="(rule as any)[field.key] ?? ''"
          :step="field.step ? parseFloat(field.step) : 1"
          :min="field.min"
          style="width: 100%"
          @change="(v) => setField(field.key, Number(v) || 0)"
        />
        <a-checkbox
          v-else-if="field.type === 'boolean'"
          :checked="!!(rule as any)[field.key]"
          @change="(checked) => setField(field.key, checked)"
        >
          {{ (rule as any)[field.key] ? '是' : '否' }}
        </a-checkbox>
      </div>
    </div>

    <details class="raw-toggle">
      <summary>查看/编辑原始 JSON</summary>
      <textarea
        :value="rawText"
        class="raw-input"
        rows="6"
        @input="onRawInput"
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
.raw-toggle {
  margin-top: 0.75rem;
  color: var(--text-muted);
  font-size: 0.8rem;
}
.raw-toggle summary { cursor: pointer; }
.raw-input {
  width: 100%;
  padding: 0.5rem;
  background: rgba(255,255,255,0.35);
  color: var(--text-primary);
  border: 1px solid var(--glass-border);
  border-radius: 0 0 4px 4px;
  font-family: monospace;
  font-size: 0.8rem;
  box-sizing: border-box;
}
</style>
