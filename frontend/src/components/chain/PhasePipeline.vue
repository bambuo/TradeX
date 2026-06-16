<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { StrategySchema, PhaseInfo, NodeDescriptor } from '../../api/strategies'
import type { ChainDefinition } from './types'
import NodeSelector from './NodeSelector.vue'
import NodeParamEditor from './NodeParamEditor.vue'

const props = defineProps<{
  chain: ChainDefinition
  schema: StrategySchema
  phase: PhaseInfo
  globalEmitNames?: string[]
}>()

const emit = defineEmits<{
  addNode: [nodeKind: string, phase: number]
  removeNode: [index: number]
  updateNodeParams: [phaseLocalIndex: number, params: Record<string, unknown>]
  moveNode: [fromIndex: number, toIndex: number]
}>()

const dragIndex = ref<number | null>(null)

/** 当前 Phase 内节点的描述符缓存（按 phaseLocalIndex 映射） */
const phaseNodeDescs = computed(() => {
  return props.chain.nodes
    .map((n, globalIdx) => ({ globalIdx, desc: props.schema.nodes.find(d => d.kind === n.nodeKind) }))
    .filter(({ desc }) => desc?.phase === props.phase.value)
})

/** 当前 Phase 内的节点列表（含 globalIdx 引用） */
const nodes = computed(() => {
  return phaseNodeDescs.value.map(({ globalIdx, desc }) => ({
    globalIdx,
    node: props.chain.nodes[globalIdx],
    desc: desc!
  }))
})

/** 展开编辑的节点索引（phaseLocalIndex） */
const expandedIndex = ref<number | null>(null)

/** 节点数量变化时自动展开首个有错误的节点 */
watch(() => nodes.value.length, (newLen, oldLen) => {
  if (newLen > oldLen) {
    // 新添加了节点，找到第一个有未配置必填参数的节点展开
    const idx = nodes.value.findIndex((_, i) =>
      hasErrors(nodes.value[i].node.nodeKind, nodes.value[i].node.params)
    )
    if (idx >= 0) {
      expandedIndex.value = idx
    }
  }
})

/** 获取节点是否有验证错误 */
function hasErrors(nodeKind: string, params: Record<string, unknown>): boolean {
  const desc = props.schema.nodes.find(d => d.kind === nodeKind)
  if (!desc) return false
  for (const p of desc.params) {
    if (p.required) {
      const val = params[p.name]
      if (val === undefined || val === null || val === '') return true
    }
    const num = Number(params[p.name])
    if (p.type === 'int' || p.type === 'float') {
      if (p.min != null && num < p.min) return true
      if (p.max != null && num > p.max) return true
    }
  }
  return false
}

/** 判断节点是否包含未配置的必填参数 */
function isUnconfigured(nodeKind: string, params: Record<string, unknown>): boolean {
  const desc = props.schema.nodes.find(d => d.kind === nodeKind)
  if (!desc) return false
  return desc.params.some(p =>
    p.required && (params[p.name] === undefined || params[p.name] === null || params[p.name] === '')
  )
}

/** 获取上游节点的 emitNames（同链内优先级更低的节点 + 跨链全局 emitNames） */
function getUpstreamEmitNames(phaseLocalIndex: number): string[] {
  const result: string[] = []
  // 同链内上游
  for (let i = 0; i < phaseLocalIndex; i++) {
    if (i < nodes.value.length) {
      const desc = nodes.value[i].desc
      if (desc.emitNames) result.push(...desc.emitNames)
    }
  }
  // 跨链全局
  if (props.globalEmitNames) {
    result.push(...props.globalEmitNames)
  }
  return result
}

function toggleExpand(index: number) {
  expandedIndex.value = expandedIndex.value === index ? null : index
}

function updateParams(phaseLocalIndex: number, params: Record<string, unknown>) {
  emit('updateNodeParams', phaseLocalIndex, params)
}

function getNodeDesc(kind: string): NodeDescriptor | undefined {
  return props.schema.nodes.find(d => d.kind === kind)
}

/** 获取节点中已配置（非 null/undefined/空）的参数 */
function visibleParams(node: { nodeKind: string; params: Record<string, unknown> }): Record<string, unknown> {
  const result: Record<string, unknown> = {}
  for (const [key, val] of Object.entries(node.params)) {
    if (val !== null && val !== undefined && val !== '') {
      result[key] = val
    }
  }
  return result
}

function hasVisibleParams(node: { nodeKind: string; params: Record<string, unknown> }): boolean {
  return Object.keys(visibleParams(node)).length > 0
}

function formatParamVal(val: unknown): string {
  if (Array.isArray(val)) return val.join(', ')
  if (typeof val === 'boolean') return val ? '是' : '否'
  return String(val)
}

// ───── Drag & Drop ─────

function onDragStart(e: DragEvent, index: number) {
  dragIndex.value = index
  if (e.dataTransfer) {
    e.dataTransfer.effectAllowed = 'move'
    e.dataTransfer.setData('text/plain', String(index))
  }
}

function onDragOver(e: DragEvent) {
  e.preventDefault()
  if (e.dataTransfer) e.dataTransfer.dropEffect = 'move'
}

function onDrop(e: DragEvent, targetIndex: number) {
  e.preventDefault()
  const from = dragIndex.value
  dragIndex.value = null
  if (from !== null && from !== targetIndex) {
    emit('moveNode', from, targetIndex)
  }
}

function onDragEnd() {
  dragIndex.value = null
}
</script>

<template>
  <div class="phase-container">
    <div class="phase-header">
      <span class="phase-label">{{ phase.label }}</span>
      <span class="phase-desc">{{ phase.description }}</span>
      <div class="phase-actions">
        <a-popover trigger="click" position="right">
          <template #content>
            <NodeSelector
              :schema="schema"
              :phase="phase.value"
              @select="(k: string) => emit('addNode', k, phase.value)"
            />
          </template>
          <a-button size="mini" type="text">
            <template #icon><icon-plus /></template>
          </a-button>
        </a-popover>
      </div>
    </div>

    <div class="phase-body" :class="{ empty: nodes.length === 0 }">
      <div v-if="nodes.length === 0" class="empty-hint">
        点击 + 添加节点
      </div>

      <!-- 拖拽放置指示器（在第一个节点之前） -->
      <div
        v-if="dragIndex !== null && nodes.length > 0"
        class="drop-indicator"
        @dragover="onDragOver"
        @drop="(e) => onDrop(e, 0)"
      />

      <div
        v-for="(item, i) in nodes"
        :key="item.globalIdx"
        class="node-card"
        :class="{
          expanded: expandedIndex === i,
          'has-errors': hasErrors(item.node.nodeKind, item.node.params) && !isUnconfigured(item.node.nodeKind, item.node.params),
          'needs-config': isUnconfigured(item.node.nodeKind, item.node.params),
          'drag-over': dragIndex !== null && dragIndex !== i,
          'dragging': dragIndex === i
        }"
        :draggable="expandedIndex !== i"
        @dragstart="(e) => onDragStart(e, i)"
        @dragover="onDragOver"
        @dragend="onDragEnd"
        @drop="(e) => onDrop(e, i + 1)"
      >
        <div class="drag-handle" @click.stop>
          <icon-drag-dot-vertical />
        </div>
        <div class="node-card-header" @click="toggleExpand(i)">
          <div class="node-card-title">
            <span class="node-kind-label">{{ item.node.nodeKind }}</span>
            <span v-if="hasErrors(item.node.nodeKind, item.node.params) && !isUnconfigured(item.node.nodeKind, item.node.params)" class="error-badge">
              <icon-exclamation-circle-fill />
            </span>
            <span v-else-if="isUnconfigured(item.node.nodeKind, item.node.params)" class="warn-badge">
              <icon-exclamation-circle-fill />
            </span>
          </div>
          <div class="node-card-actions">
            <a-button
              size="mini"
              type="text"
              status="danger"
              @click.stop="emit('removeNode', i)"
            >
              <template #icon><icon-delete /></template>
            </a-button>
          </div>
        </div>

        <div class="node-card-desc">
          {{ item.desc.description }}
        </div>

        <!-- 展开参数编辑 -->
        <div v-if="expandedIndex === i" class="node-params-editor">
          <div class="params-divider" />
          <NodeParamEditor
            v-for="pd in item.desc.params"
            :key="pd.name"
            :params="item.node.params"
            :descriptor="pd"
            :upstream-emit-names="getUpstreamEmitNames(i)"
            @update="(val: unknown) => {
              const newParams = { ...item.node.params, [pd.name]: val }
              updateParams(i, newParams)
            }"
          />
        </div>

        <!-- 折叠时显示参数摘要 -->
        <div v-else-if="hasVisibleParams(item.node)" class="node-card-params">
          <div v-for="(val, key) in visibleParams(item.node)" :key="key" class="param-row">
            <span class="param-key">{{ key }}:</span>
            <span class="param-val">{{ formatParamVal(val) }}</span>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.phase-container {
  border: 1px solid var(--color-border-2);
  border-radius: 8px;
  overflow: hidden;
}
.phase-header {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 10px;
  background: var(--color-fill-2);
  font-size: 12px;
}
.phase-label {
  font-weight: 600;
  color: var(--color-text-1);
  font-size: 13px;
}
.phase-desc {
  flex: 1;
  color: var(--color-text-3);
  font-size: 11px;
}
.phase-actions {
  flex-shrink: 0;
}
.phase-body {
  padding: 8px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-height: 48px;
}
.phase-body.empty {
  display: flex;
  align-items: center;
  justify-content: center;
}
.empty-hint {
  color: var(--color-text-4);
  font-size: 12px;
}
.drop-indicator {
  height: 4px;
  border-radius: 2px;
  background: rgb(var(--arcoblue-5));
  transition: height 0.1s;
}
.node-card {
  display: flex;
  flex-direction: column;
  border: 1px solid var(--color-border-2);
  border-radius: 6px;
  padding: 6px 8px 6px 4px;
  background: var(--color-bg-1);
  transition: box-shadow 0.15s, opacity 0.15s;
  cursor: pointer;
}
.node-card:hover {
  border-color: var(--color-border-3);
}
.node-card.expanded {
  border-color: rgb(var(--arcoblue-5));
  box-shadow: 0 0 0 1px rgba(var(--arcoblue-5), 0.2);
}
.node-card.has-errors {
  border-color: rgb(var(--red-5));
}
.node-card.dragging {
  opacity: 0.4;
}
.node-card.drag-over {
  border-color: rgb(var(--arcoblue-5));
  box-shadow: 0 0 0 1px rgba(var(--arcoblue-5), 0.3);
}
.drag-handle {
  display: flex;
  align-items: center;
  color: var(--color-text-4);
  cursor: grab;
  margin-bottom: 2px;
  font-size: 14px;
}
.drag-handle:active {
  cursor: grabbing;
}
.node-card-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}
.node-card-title {
  display: flex;
  align-items: center;
  gap: 4px;
}
.node-kind-label {
  font-family: monospace;
  font-size: 12px;
  font-weight: 500;
  color: rgb(var(--arcoblue-6));
}
.node-card.needs-config {
  border-color: rgb(var(--orange-6));
}
.error-badge {
  color: rgb(var(--red-5));
  font-size: 14px;
  display: flex;
}
.warn-badge {
  color: rgb(var(--orange-6));
  font-size: 14px;
  display: flex;
}
.node-card-actions {
  flex-shrink: 0;
}
.node-card-desc {
  font-size: 11px;
  color: var(--color-text-3);
  margin-top: 2px;
}
.node-params-editor {
  margin-top: 4px;
}
.params-divider {
  height: 1px;
  background: var(--color-border-1);
  margin-bottom: 4px;
}
.node-card-params {
  margin-top: 4px;
  padding-top: 4px;
  border-top: 1px solid var(--color-border-1);
  display: flex;
  flex-wrap: wrap;
  gap: 4px 12px;
}
.param-row {
  font-size: 11px;
}
.param-key {
  color: var(--color-text-3);
  margin-right: 2px;
}
.param-val {
  color: var(--color-text-1);
  font-family: monospace;
}
</style>
