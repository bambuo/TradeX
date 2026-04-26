<script setup lang="ts">
import { computed } from 'vue'
import AppSelect from './AppSelect.vue'

export interface ConditionNode {
  operator?: string
  conditions?: ConditionNode[]
  indicator?: string
  parameters?: Record<string, number>
  comparison?: string
  value?: number
}

const props = defineProps<{
  node: ConditionNode
  depth?: number
}>()

const emit = defineEmits<{
  update: [node: ConditionNode]
}>()

const isLeaf = computed(() => !props.node.conditions || props.node.conditions.length === 0)
const operators = ['AND', 'OR', 'NOT']
const indicators = ['RSI', 'SMA_20', 'SMA_50', 'EMA_20', 'MACD_LINE', 'MACD_SIGNAL', 'BB', 'OBV', 'VolumeSMA']
const comparisons = ['>', '<', '>=', '<=', '==', 'CrossAbove', 'CrossBelow']

function removeFromParent() {
  emit('update', null as any)
}

function updateChild(index: number, child: ConditionNode | null) {
  if (child === null) {
    const updated = { ...props.node }
    updated.conditions = [...(updated.conditions || [])]
    updated.conditions.splice(index, 1)
    emit('update', updated)
  } else {
    const updated = { ...props.node }
    updated.conditions = [...(updated.conditions || [])]
    updated.conditions[index] = child
    emit('update', updated)
  }
}

function updateField(field: string, value: any) {
  emit('update', { ...props.node, [field]: value })
}
</script>

<template>
  <div class="condition-node" :class="{ leaf: isLeaf, root: !depth }" :style="{ marginLeft: depth ? '12px' : '0' }">
    <!-- Logical operator node -->
    <template v-if="!isLeaf">
      <div class="node-header">
        <AppSelect
          :options="operators.map(o => ({ label: o, value: o }))"
          :model-value="node.operator ?? 'AND'"
          class="op-select"
          @update:model-value="(v: string) => updateField('operator', v)"
        />
        <AppButton size="sm" variant="ghost" icon="close" title="删除" @click="removeFromParent" />
      </div>
      <div class="children">
        <ConditionTreeEditor
          v-for="(child, i) in node.conditions"
          :key="i"
          :node="child"
          :depth="(depth || 0) + 1"
          @update="(c: any) => updateChild(i, c)"
        />
      </div>
      <div class="add-actions">
        <AppButton size="sm" icon="plus" @click="node.conditions!.push({ operator: 'AND', conditions: [] })">条件组</AppButton>
        <AppButton size="sm" icon="plus" @click="node.conditions!.push({ indicator: 'RSI', comparison: '>', value: 50 })">条件</AppButton>
      </div>
    </template>

    <!-- Leaf condition node -->
    <template v-else>
      <div class="leaf-editor">
        <AppSelect
          :options="indicators.map(i => ({ label: i, value: i }))"
          :model-value="node.indicator ?? indicators[0]"
          @update:model-value="(v: string) => updateField('indicator', v)"
        />
        <AppSelect
          :options="comparisons.map(c => ({ label: c, value: c }))"
          :model-value="node.comparison ?? comparisons[0]"
          narrow
          @update:model-value="(v: string) => updateField('comparison', v)"
        />
        <input :value="node.value" type="number" step="any" class="field-input" placeholder="值" @input="(e) => updateField('value', parseFloat((e.target as HTMLInputElement).value) || 0)" />
        <AppButton size="sm" variant="ghost" icon="close" title="删除" @click="removeFromParent" />
      </div>
    </template>
  </div>
</template>

<style scoped>
.condition-node {
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  padding: 10px;
  background: rgba(255,255,255,0.55);
  margin-bottom: 6px;
}
.condition-node.leaf {
  background: rgba(255,255,255,0.35);
}
.node-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 8px;
}
.op-select :deep(.app-select-trigger) {
  font-weight: 700;
  font-size: 0.85rem;
  color: var(--accent-blue);
  border-color: var(--accent-blue);
}
.children {
  display: flex;
  flex-direction: column;
  gap: 4px;
}
.add-actions {
  display: flex;
  gap: 6px;
  margin-top: 8px;
}

.leaf-editor {
  display: flex;
  gap: 6px;
  align-items: center;
  flex-wrap: wrap;
}

.field-input {
  width: 80px;
  padding: 4px 6px;
  background: rgba(255,255,255,0.55);
  color: var(--text-primary);
  border: 1px solid var(--glass-border-strong);
  border-radius: 4px;
  font-size: 0.8rem;
}
</style>
