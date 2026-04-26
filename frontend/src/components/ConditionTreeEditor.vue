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
        <select :value="node.operator" class="op-select" @change="(e) => updateField('operator', (e.target as HTMLSelectElement).value)">
          <option v-for="op in operators" :key="op" :value="op">{{ op }}</option>
        </select>
        <button class="btn-icon" title="删除" @click="removeFromParent">✕</button>
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
        <button class="btn-sm" @click="node.conditions!.push({ operator: 'AND', conditions: [] })">＋ 条件组</button>
        <button class="btn-sm" @click="node.conditions!.push({ indicator: 'RSI', comparison: '>', value: 50 })">＋ 条件</button>
      </div>
    </template>

    <!-- Leaf condition node -->
    <template v-else>
      <div class="leaf-editor">
        <select :value="node.indicator" class="field-select" @change="(e) => updateField('indicator', (e.target as HTMLSelectElement).value)">
          <option v-for="ind in indicators" :key="ind" :value="ind">{{ ind }}</option>
        </select>
        <select :value="node.comparison" class="field-select narrow" @change="(e) => updateField('comparison', (e.target as HTMLSelectElement).value)">
          <option v-for="cmp in comparisons" :key="cmp" :value="cmp">{{ cmp }}</option>
        </select>
        <input :value="node.value" type="number" step="any" class="field-input" placeholder="值" @input="(e) => updateField('value', parseFloat((e.target as HTMLInputElement).value) || 0)" />
        <button class="btn-icon" title="删除" @click="removeFromParent">✕</button>
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
  border-color: #475569;
}
.node-header {
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 8px;
}
.op-select {
  padding: 3px 6px;
  background: rgba(255,255,255,0.35);
  color: var(--accent-blue);
  border: 1px solid var(--accent-blue);
  border-radius: 4px;
  font-weight: 700;
  font-size: 0.85rem;
  cursor: pointer;
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
.btn-sm {
  padding: 3px 10px;
  background: #334155;
  color: var(--text-muted);
  border: 1px dashed #475569;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.75rem;
}
.btn-sm:hover { color: var(--text-primary); border-color: var(--accent-blue); }
.btn-icon {
  width: 22px;
  height: 22px;
  display: flex;
  align-items: center;
  justify-content: center;
  background: transparent;
  color: var(--accent-red);
  border: none;
  border-radius: 4px;
  cursor: pointer;
  font-size: 0.8rem;
  margin-left: auto;
}
.btn-icon:hover { background: rgba(239,68,68,0.1); }
.leaf-editor {
  display: flex;
  gap: 6px;
  align-items: center;
  flex-wrap: wrap;
}
.field-select {
  padding: 4px 6px;
  background: rgba(255,255,255,0.55);
  color: var(--text-primary);
  border: 1px solid var(--glass-border-strong);
  border-radius: 4px;
  font-size: 0.8rem;
  cursor: pointer;
}
.field-select.narrow { width: 100px; }
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
