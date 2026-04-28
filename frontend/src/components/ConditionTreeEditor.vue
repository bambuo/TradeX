<script setup lang="ts">
import { computed } from 'vue'

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
const indicators = ['RSI', 'SMA_20', 'SMA_50', 'EMA_20', 'MACD_LINE', 'MACD_SIGNAL', 'BB_UPPER', 'BB_LOWER', 'OBV', 'VOLUME_SMA', 'RANGE_PCT']
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
        <a-select
          :model-value="node.operator ?? 'AND'"
          class="op-select"
          size="small"
          @change="(v) => updateField('operator', v)"
        >
          <a-option v-for="op in operators" :key="op" :value="op" :label="op" />
        </a-select>
        <a-button size="mini" type="text" @click="removeFromParent">
          <template #icon><icon-close /></template>
        </a-button>
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
        <a-button size="mini" @click="node.conditions!.push({ operator: 'AND', conditions: [] })">
          <template #icon><icon-plus /></template>
          条件组
        </a-button>
        <a-button size="mini" @click="node.conditions!.push({ indicator: 'RSI', comparison: '>', value: 50 })">
          <template #icon><icon-plus /></template>
          条件
        </a-button>
      </div>
    </template>

    <!-- Leaf condition node -->
    <template v-else>
      <div class="leaf-editor">
        <a-select
          :model-value="node.indicator ?? indicators[0]"
          size="small"
          style="width: 130px"
          @change="(v) => updateField('indicator', v)"
        >
          <a-option v-for="ind in indicators" :key="ind" :value="ind" :label="ind" />
        </a-select>
        <a-select
          :model-value="node.comparison ?? comparisons[0]"
          size="small"
          style="width: 110px"
          @change="(v) => updateField('comparison', v)"
        >
          <a-option v-for="cmp in comparisons" :key="cmp" :value="cmp" :label="cmp" />
        </a-select>
        <a-input-number :model-value="node.value" class="field-input" placeholder="值" @change="(v) => updateField('value', Number(v) || 0)" />
        <a-button size="mini" type="text" @click="removeFromParent">
          <template #icon><icon-close /></template>
        </a-button>
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
.op-select {
  font-weight: 700;
  font-size: 0.85rem;
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
}
</style>
