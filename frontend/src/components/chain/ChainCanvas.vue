<script setup lang="ts">
import { computed, ref, h } from 'vue'
import { Message, Modal } from '@arco-design/web-vue'
import type { StrategySchema } from '../../api/strategies'
import type { ChainDefinition, NodeInstance } from './types'
import { createDefaultChain, createChainKey } from './types'
import PhasePipeline from './PhasePipeline.vue'
import PhaseArrow from './PhaseArrow.vue'

const props = defineProps<{
  modelValue: string
  schema: StrategySchema
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const chains = computed({
  get: (): ChainDefinition[] => {
    try {
      const parsed = JSON.parse(props.modelValue)
      return Array.isArray(parsed) ? parsed : [createDefaultChain()]
    } catch {
      return [createDefaultChain()]
    }
  },
  set: (val: ChainDefinition[]) => {
    emit('update:modelValue', JSON.stringify(val))
  }
})

const selectedIndex = ref(0)
const selectedChain = computed(() => chains.value[selectedIndex.value] ?? chains.value[0])
const editingName = ref('')
const editingNameKey = ref('')

/** 所有链的全局 emitNames（EmitScope 为 global 或未指定时跨链可见） */
const globalEmitNames = computed(() => {
  const names = new Set<string>()
  for (const chain of chains.value) {
    for (const node of chain.nodes) {
      const desc = props.schema.nodes.find(d => d.kind === node.nodeKind)
      if (desc?.emitNames) {
        for (const name of desc.emitNames) {
          // 没有明确 scope 的 emitName 默认可跨链引用
          if (!desc.emitScope || desc.emitScope === 'global') {
            names.add(name)
          }
        }
      }
    }
  }
  return [...names]
})

function selectChain(index: number) {
  selectedIndex.value = index
}

function addChain() {
  const chain = createDefaultChain(`链 ${chains.value.length + 1}`)
  const updated = [...chains.value, chain]
  chains.value = updated
  selectedIndex.value = updated.length - 1
}

function removeChain(index: number) {
  if (chains.value.length <= 1) return
  const updated = chains.value.filter((_, i) => i !== index)
  chains.value = updated
  if (selectedIndex.value >= updated.length) {
    selectedIndex.value = updated.length - 1
  }
}

function startRename(chain: ChainDefinition) {
  editingName.value = chain.name
  editingNameKey.value = chain.key
}

function finishRename() {
  if (!editingNameKey.value) return
  const updated = [...chains.value]
  const idx = updated.findIndex(c => c.key === editingNameKey.value)
  if (idx >= 0) {
    updated[idx] = { ...updated[idx], name: editingName.value || '未命名' }
    chains.value = updated
  }
  editingNameKey.value = ''
  editingName.value = ''
}

function addNode(nodeKind: string, phase: number) {
  const updated = [...chains.value]
  const idx = selectedIndex.value
  const maxPriority = updated[idx].nodes
    .filter(n => {
      const desc = props.schema.nodes.find(d => d.kind === n.nodeKind)
      return desc?.phase === phase
    })
    .reduce((max, n) => Math.max(max, n.priority), 0)

  // 从 schema 中预填默认参数值
  const desc = props.schema.nodes.find(d => d.kind === nodeKind)
  const defaultParams: Record<string, unknown> = {}
  if (desc) {
    for (const pd of desc.params) {
      if (pd.default !== undefined) {
        defaultParams[pd.name] = pd.default
      }
    }
  }

  updated[idx] = {
    ...updated[idx],
    nodes: [
      ...updated[idx].nodes,
      { nodeKind, params: defaultParams, priority: maxPriority + 10 }
    ]
  }
  chains.value = updated
}

function removeNode(nodeIndex: number) {
  const chain = chains.value[selectedIndex.value]
  const globalIdx = phaseLocalToGlobal(chain, nodeIndex)
  if (globalIdx < 0) return

  const removedNode = chain.nodes[globalIdx]
  const removedDesc = props.schema.nodes.find(d => d.kind === removedNode.nodeKind)

  // 检查是否有下游节点依赖此节点的 emitNames
  const deps = findDepReferences(chain, globalIdx, removedDesc?.emitNames ?? [])
  if (deps.length > 0) {
    Modal.confirm({
      title: '删除节点确认',
      content: () => {
        const name = removedNode.nodeKind
        const items = deps.map(d => `${d.nodeKind} -> ${d.refName}`).join('\n')
        return h('div', [
          h('p', `节点 [${name}] 的以下信号被其他节点引用：`),
          h('ul', { style: { marginTop: 8, paddingLeft: 20, color: 'var(--color-text-2)', fontSize: 13 } },
            deps.map(d => h('li', `${d.nodeKind} 引用了 [${d.refName}]`))
          ),
          h('p', { style: { marginTop: 8 } }, '确定删除吗？删除后引用方参数将失效。')
        ])
      },
      okButtonProps: { status: 'danger' },
      onOk: () => doRemoveNode(selectedIndex.value, globalIdx, removedNode)
    })
  } else {
    doRemoveNode(selectedIndex.value, globalIdx, removedNode)
  }
}

function doRemoveNode(chainIdx: number, globalIdx: number, removedNode: NodeInstance) {
  const updated = [...chains.value]
  updated[chainIdx] = {
    ...updated[chainIdx],
    nodes: updated[chainIdx].nodes.filter((_, i) => i !== globalIdx)
  }
  chains.value = updated

  // Undo Toast
  Message.info({
    content: () => h('span', [
      `已删除节点 [${removedNode.nodeKind}] `,
      h('a-button', {
        size: 'mini',
        type: 'primary',
        style: { marginLeft: 12 },
        onClick: () => undoRemove(chainIdx, globalIdx, removedNode)
      }, () => '撤销')
    ]),
    duration: 5000
  })
}

function undoRemove(chainIdx: number, globalIdx: number, node: NodeInstance) {
  const updated = [...chains.value]
  const nodes = [...updated[chainIdx].nodes]
  nodes.splice(globalIdx, 0, node)
  updated[chainIdx] = { ...updated[chainIdx], nodes }
  chains.value = updated
  Message.success('已恢复删除')
}

/** 查找引用指定 emitNames 的所有下游节点 */
function findDepReferences(chain: ChainDefinition, excludeGlobalIdx: number, emitNames: string[]): { nodeKind: string; refName: string }[] {
  if (emitNames.length === 0) return []
  const refs: { nodeKind: string; refName: string }[] = []
  chain.nodes.forEach((n, gi) => {
    if (gi === excludeGlobalIdx) return
    const desc = props.schema.nodes.find(d => d.kind === n.nodeKind)
    if (!desc) return
    for (const pd of desc.params) {
      if (pd.type === 'ref' && emitNames.includes(String(n.params[pd.name]))) {
        refs.push({ nodeKind: n.nodeKind, refName: String(n.params[pd.name]) })
      }
    }
  })
  return refs
}

/** 拖拽移动节点：在同 Phase 内调整顺序 */
function moveNode(fromLocalIndex: number, toLocalIndex: number) {
  const chain = chains.value[selectedIndex.value]
  const chainIdx = selectedIndex.value
  const phaseMap = buildPhaseMap(chain)

  // 收集当前 Phase 的所有 globalIndex，按显示顺序排列
  const phases = [...phaseMap.entries()].sort((a, b) => a[0] - b[0])
  let allPhaseIndices: number[] = []
  for (const [, indices] of phases) {
    allPhaseIndices.push(...indices)
  }

  // 从数组中取出该 Phase 的节点，重排后放回
  const phaseNodesArr = allPhaseIndices.map(gi => chain.nodes[gi])
  const [moved] = phaseNodesArr.splice(fromLocalIndex, 1)
  phaseNodesArr.splice(Math.min(toLocalIndex, phaseNodesArr.length), 0, moved)

  // 重构完整 nodes 数组
  const phaseSet = new Set(allPhaseIndices)
  const result: NodeInstance[] = []
  let phasePos = 0
  for (let i = 0; i < chain.nodes.length; i++) {
    result.push(phaseSet.has(i) ? { ...phaseNodesArr[phasePos++], priority: (phasePos) * 10 } : chain.nodes[i])
  }

  const updated = [...chains.value]
  updated[chainIdx] = { ...updated[chainIdx], nodes: result }
  chains.value = updated
}

/** 判断指定 Phase 索引是否有节点 */
function phaseHasNodes(phaseIdx: number): boolean {
  const phase = props.schema.phases[phaseIdx]
  if (!phase) return false
  return selectedChain.value.nodes.some(n => {
    const desc = props.schema.nodes.find(d => d.kind === n.nodeKind)
    return desc?.phase === phase.value
  })
}

/** 检测在链中添加 ref 是否会形成循环引用 */
function detectCycle(chainIdx: number, fromNodeGlobalIdx: number, refTargetEmitName: string): string | null {
  const chain = chains.value[chainIdx]
  // 找到 emitName 对应的源节点
  let sourceGlobalIdx = -1
  for (let i = 0; i < chain.nodes.length; i++) {
    const desc = props.schema.nodes.find(d => d.kind === chain.nodes[i].nodeKind)
    if (desc?.emitNames?.includes(refTargetEmitName)) {
      sourceGlobalIdx = i
      break
    }
  }
  if (sourceGlobalIdx < 0) return null // emitName not found in chain

  // 构建依赖图：从 source 开始 DFS，检查是否能到达 fromNode
  const visited = new Set<number>()
  const stack = [sourceGlobalIdx]
  while (stack.length > 0) {
    const idx = stack.pop()!
    if (idx === fromNodeGlobalIdx) return `检测到循环引用：节点 [${chain.nodes[fromNodeGlobalIdx].nodeKind}] → ... → [${chain.nodes[idx].nodeKind}]`
    if (visited.has(idx)) continue
    visited.add(idx)
    // 添加 idx 的所有 ref 依赖
    const desc = props.schema.nodes.find(d => d.kind === chain.nodes[idx].nodeKind)
    if (desc) {
      for (const pd of desc.params) {
        if (pd.type === 'ref') {
          const refVal = String(chain.nodes[idx].params[pd.name] ?? '')
          if (refVal) {
            const targetIdx = chain.nodes.findIndex((n, ti) => {
              const td = props.schema.nodes.find(d => d.kind === n.nodeKind)
              return td?.emitNames?.includes(refVal)
            })
            if (targetIdx >= 0 && !visited.has(targetIdx)) {
              stack.push(targetIdx)
            }
          }
        }
      }
    }
  }
  return null
}

function buildPhaseMap(chain: ChainDefinition): Map<number, number[]> {
  const map = new Map<number, number[]>()
  chain.nodes.forEach((n, i) => {
    const desc = props.schema.nodes.find(d => d.kind === n.nodeKind)
    const ph = desc?.phase ?? 0
    if (!map.has(ph)) map.set(ph, [])
    map.get(ph)!.push(i)
  })
  return map
}

function updateNodeParams(phaseLocalIndex: number, params: Record<string, unknown>) {
  const updated = [...chains.value]
  const idx = selectedIndex.value
  const globalIdx = phaseLocalToGlobal(updated[idx], phaseLocalIndex)
  if (globalIdx < 0) return

  // 检查 ref 类型参数是否导致循环引用
  const prevParams = updated[idx].nodes[globalIdx].params
  for (const [key, val] of Object.entries(params)) {
    const desc = props.schema.nodes.find(d => d.kind === updated[idx].nodes[globalIdx].nodeKind)
    const pd = desc?.params.find(p => p.name === key)
    if (pd?.type === 'ref' && val && val !== prevParams[key]) {
      const cycle = detectCycle(idx, globalIdx, String(val))
      if (cycle) {
        Message.warning(cycle)
        return // 不应用此变更
      }
    }
  }

  const nodes = [...updated[idx].nodes]
  nodes[globalIdx] = { ...nodes[globalIdx], params }
  updated[idx] = { ...updated[idx], nodes }
  chains.value = updated
}

/** 将 phaseLocalIndex 转换为链 nodes 数组的 globalIndex */
function phaseLocalToGlobal(chain: ChainDefinition, phaseLocalIndex: number): number {
  const phaseMap = new Map<number, number[]>()
  chain.nodes.forEach((n, i) => {
    const desc = props.schema.nodes.find(d => d.kind === n.nodeKind)
    const ph = desc?.phase ?? 0
    if (!phaseMap.has(ph)) phaseMap.set(ph, [])
    phaseMap.get(ph)!.push(i)
  })

  const phases = [...phaseMap.entries()].sort((a, b) => a[0] - b[0])
  let globalIdx = -1
  let count = 0
  for (const [, indices] of phases) {
    if (count + indices.length > phaseLocalIndex) {
      globalIdx = indices[phaseLocalIndex - count]
      break
    }
    count += indices.length
  }
  return globalIdx
}
</script>

<template>
  <div class="chain-canvas">
    <!-- 左侧链列表 -->
    <div class="chain-sidebar">
      <div class="sidebar-header">
        <span class="sidebar-title">规则链</span>
        <a-button size="mini" type="text" @click="addChain">
          <template #icon><icon-plus /></template>
        </a-button>
      </div>
      <div v-for="(chain, i) in chains" :key="chain.key" class="chain-item-wrapper">
        <div
          class="chain-item"
          :class="{ active: i === selectedIndex }"
          @click="selectChain(i)"
        >
          <template v-if="editingNameKey === chain.key">
            <a-input
              v-model="editingName"
              size="mini"
              @blur="finishRename"
              @keyup.enter="finishRename"
              @keyup.escape="finishRename"
            />
          </template>
          <template v-else>
            <span class="chain-name" @dblclick="startRename(chain)">{{ chain.name }}</span>
          </template>
          <a-button
            v-if="chains.length > 1"
            size="mini"
            type="text"
            status="danger"
            @click.stop="removeChain(i)"
          >
            <template #icon><icon-close /></template>
          </a-button>
        </div>
      </div>
    </div>

    <!-- 右侧流水线画布 -->
    <div class="pipeline-area">
      <div class="pipeline-scroll">
        <template v-for="(phase, pi) in schema.phases" :key="phase.value">
          <PhaseArrow
            v-if="pi > 0"
            :from-phase="schema.phases[pi - 1].label"
            :to-phase="phase.label"
            :visible="phaseHasNodes(pi - 1) && phaseHasNodes(pi)"
          />
          <PhasePipeline
            :chain="selectedChain"
            :schema="schema"
            :phase="phase"
            :global-emit-names="globalEmitNames"
            @add-node="addNode"
            @remove-node="removeNode"
            @update-node-params="updateNodeParams"
            @move-node="moveNode"
          />
        </template>
      </div>
    </div>
  </div>
</template>

<style scoped>
.chain-canvas {
  display: flex;
  gap: 12px;
  max-height: 520px;
  min-height: 240px;
  flex: 1;
  border: 1px solid var(--color-border-2);
  border-radius: 8px;
  overflow: hidden;
}

.chain-sidebar {
  width: 180px;
  flex-shrink: 0;
  border-right: 1px solid var(--color-border-2);
  display: flex;
  flex-direction: column;
  overflow-y: auto;
  height: 100%;
}

.sidebar-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 8px 10px;
  border-bottom: 1px solid var(--color-border-1);
}

.sidebar-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--color-text-1);
}

.chain-item-wrapper {
  padding: 2px 4px;
}

.chain-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 6px 8px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 12px;
  transition: background 0.15s;
}

.chain-item:hover {
  background: var(--color-fill-2);
}

.chain-item.active {
  background: rgba(var(--arcoblue-1), 0.6);
  color: rgb(var(--arcoblue-6));
}

.chain-name {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.pipeline-area {
  flex: 1;
  overflow-y: auto;
  min-height: 0;
}

.pipeline-scroll {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 10px;
}
</style>
