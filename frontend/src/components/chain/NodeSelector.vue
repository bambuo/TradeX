<script setup lang="ts">
import { computed, ref } from 'vue'
import type { StrategySchema, NodeDescriptor } from '../../api/strategies'

const props = defineProps<{
  schema: StrategySchema
  phase: number
}>()

const emit = defineEmits<{
  select: [nodeKind: string]
}>()

const search = ref('')

const availableNodes = computed(() => {
  let nodes = props.schema.nodes.filter(n => n.phase === props.phase)
  if (search.value) {
    const q = search.value.toLowerCase()
    nodes = nodes.filter(n =>
      n.kind.toLowerCase().includes(q) ||
      n.description.toLowerCase().includes(q)
    )
  }
  return nodes
})

const categories = computed(() => {
  const map = new Map<string, NodeDescriptor[]>()
  for (const n of availableNodes.value) {
    if (!map.has(n.category)) map.set(n.category, [])
    map.get(n.category)!.push(n)
  }
  return [...map.entries()]
})
</script>

<template>
  <div class="node-selector">
    <a-input
      :model-value="search"
      placeholder="搜索节点..."
      size="small"
      allow-clear
      @input="search = $event"
    >
      <template #prefix><icon-search /></template>
    </a-input>

    <div v-if="categories.length === 0" class="empty-hint">
      该阶段暂无可用节点
    </div>

    <div v-for="[cat, nodes] in categories" :key="cat" class="cat-group">
      <div class="cat-title">{{ cat }}</div>
      <div
        v-for="n in nodes"
        :key="n.kind"
        class="node-item"
        @click="emit('select', n.kind)"
      >
        <div class="node-kind">{{ n.kind }}</div>
        <div class="node-desc">{{ n.description }}</div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.node-selector {
  display: flex;
  flex-direction: column;
  gap: 8px;
  min-width: 240px;
}
.empty-hint {
  color: var(--color-text-3);
  font-size: 12px;
  text-align: center;
  padding: 16px 0;
}
.cat-group { display: flex; flex-direction: column; gap: 4px; }
.cat-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--color-text-3);
  text-transform: uppercase;
  letter-spacing: 0.5px;
  padding: 4px 0;
}
.node-item {
  padding: 6px 8px;
  border: 1px solid var(--color-border-2);
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s;
}
.node-item:hover {
  border-color: rgb(var(--arcoblue-6));
  background: rgba(var(--arcoblue-1), 0.4);
}
.node-kind {
  font-size: 13px;
  font-weight: 500;
  color: var(--color-text-1);
  font-family: monospace;
}
.node-desc {
  font-size: 11px;
  color: var(--color-text-3);
  margin-top: 1px;
  line-height: 1.4;
}
</style>
