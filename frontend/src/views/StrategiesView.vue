<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { strategiesApi, type Strategy } from '../api/strategies'
import { strategyPresets } from '../api/strategyPresets'
import ConditionTreeEditor, { type ConditionNode } from '../components/ConditionTreeEditor.vue'
import ExecutionRuleEditor from '../components/ExecutionRuleEditor.vue'

const cmpLabels: Record<string, string> = {
  '>': '>', '<': '<', '>=': '≥', '<=': '≤', '==': '＝', CrossAbove: '上穿', CrossBelow: '下穿'
}

function humanizeCondition(json: string): string {
  try {
    const node: ConditionNode = JSON.parse(json)
    return renderNode(node)
  } catch { return json }
}

function renderNode(node: ConditionNode): string {
  if (!node.operator && node.indicator) {
    const cmp = cmpLabels[node.comparison ?? ''] || node.comparison || '?'
    return `${node.indicator} ${cmp} ${node.value}`
  }
  if (node.conditions && node.conditions.length > 0) {
    const opLabel = node.operator === 'AND' ? '且' : node.operator === 'OR' ? '或' : node.operator ?? ''
    const parts = node.conditions.map(renderNode)
    if (parts.length === 1) return `${opLabel}( ${parts[0]} )`
    return `( ${parts.join(` ${opLabel} `)} )`
  }
  return '无条件'
}

function humanizeExecutionRule(json: string): string {
  try {
    const obj = JSON.parse(json)
    const typeLabels: Record<string, string> = { grid: '网格', trend_following: '趋势追踪', infinity_grid: '无限网格', custom: '自定义' }
    return typeLabels[obj.type] || '自定义'
  } catch { return '自定义' }
}

const strategies = ref<Strategy[]>([])
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const showPresets = ref(false)

const activePreset = ref<typeof strategyPresets[0] | null>(null)
const formName = ref('')
const entryNode = ref<ConditionNode>({ operator: 'AND', conditions: [] })
const exitNode = ref<ConditionNode>({ operator: 'AND', conditions: [] })
const formExecutionRule = ref('{}')

function conditionToNode(json: string): ConditionNode {
  try {
    const parsed = JSON.parse(json)
    if (!parsed.operator && !parsed.conditions && !parsed.indicator) {
      return { operator: 'AND', conditions: [] }
    }
    if (!parsed.conditions) {
      return parsed
    }
    return parsed
  } catch {
    return { operator: 'AND', conditions: [] }
  }
}

function nodeToCondition(node: ConditionNode): string {
  if (!node.conditions || node.conditions.length === 0) {
    if (node.indicator) {
      return JSON.stringify(node)
    }
    return '{}'
  }
  if (node.conditions.length === 1 && !node.conditions[0].indicator && !node.conditions[0].conditions) {
    return JSON.stringify(node.conditions[0])
  }
  return JSON.stringify(node)
}

async function load() {
  loading.value = true
  try {
    const { data } = await strategiesApi.getAllPure()
    strategies.value = data.data ?? []
  } finally {
    loading.value = false
  }
}

function resetForm() {
  activePreset.value = null
  formName.value = ''
  entryNode.value = { operator: 'AND', conditions: [] }
  exitNode.value = { operator: 'AND', conditions: [] }
  formExecutionRule.value = '{}'
}

function openCreate() {
  editId.value = null
  resetForm()
  showPresets.value = true
  showForm.value = true
}

function openEdit(s: Strategy) {
  editId.value = s.id
  formName.value = s.name
  entryNode.value = conditionToNode(s.entryConditionJson)
  exitNode.value = conditionToNode(s.exitConditionJson)
  formExecutionRule.value = s.executionRuleJson
  showPresets.value = false
  showForm.value = true
}

function applyPreset(preset: typeof strategyPresets[0]) {
  formName.value = preset.name
  entryNode.value = conditionToNode(preset.entryConditionJson)
  exitNode.value = conditionToNode(preset.exitConditionJson)
  formExecutionRule.value = preset.executionRuleJson
  activePreset.value = preset
  showPresets.value = false
}

async function save() {
  const entryJson = nodeToCondition(entryNode.value)
  const exitJson = nodeToCondition(exitNode.value)

  if (editId.value) {
    await strategiesApi.updatePure(editId.value, {
      name: formName.value,
      entryConditionJson: entryJson,
      exitConditionJson: exitJson,
      executionRuleJson: formExecutionRule.value
    })
  } else {
    await strategiesApi.createPure({
      name: formName.value,
      entryConditionJson: entryJson,
      exitConditionJson: exitJson,
      executionRuleJson: formExecutionRule.value
    })
  }
  showForm.value = false
  await load()
}

async function remove(id: string) {
  await strategiesApi.deletePure(id)
  await load()
}

onMounted(load)
</script>

<template>
  <div class="strategies-page">
    <header class="page-header">
      <h2>策略模板</h2>
      <AppButton variant="primary" icon="plus" @click="openCreate">新建策略</AppButton>
    </header>

    <AppModal v-model="showForm" :title="editId ? '编辑策略模板' : '新建策略模板'" width="xl">
      <div v-if="showPresets && !editId" class="presets-section">
        <p class="presets-hint">选择一个预设模板快速创建，或直接编辑下方条件</p>
        <div class="preset-cards">
          <div
            v-for="preset in strategyPresets"
            :key="preset.name"
            class="preset-card"
            @click="applyPreset(preset)"
          >
            <strong class="preset-name">{{ preset.name }}</strong>
            <span class="preset-desc">{{ preset.description }}</span>
          </div>
        </div>
      </div>

      <div class="form-body">
        <div v-if="activePreset && !showPresets" class="notes-section">
          <span class="notes-title">最佳实践参考</span>
          <ul class="notes-list">
            <li v-for="(note, i) in activePreset.notes" :key="i">{{ note }}</li>
          </ul>
        </div>

        <input v-model="formName" placeholder="策略名称" class="input" />

        <div class="section">
          <span class="section-label">入场条件</span>
          <ConditionTreeEditor
            :node="entryNode"
            @update="(n: ConditionNode) => entryNode = n"
          />
        </div>

        <div class="section">
          <span class="section-label">出场条件</span>
          <ConditionTreeEditor
            :node="exitNode"
            @update="(n: ConditionNode) => exitNode = n"
          />
        </div>

        <div class="section">
          <span class="section-label">执行规则</span>
          <ExecutionRuleEditor v-model="formExecutionRule" />
        </div>
      </div>

      <template #footer>
        <AppButton variant="primary" icon="save" @click="save">保存</AppButton>
      </template>
    </AppModal>

    <div v-if="loading" class="loading">加载中...</div>
    <div v-else-if="strategies.length === 0" class="empty">暂无策略模板</div>
    <div v-else class="card-grid">
      <div
        v-for="s in strategies"
        :key="s.id"
        class="strategy-card"
        style="border-top-color: var(--accent-blue)"
      >
        <div class="card-header">
          <div class="card-title-area">
            <h3>{{ s.name }}</h3>
            <span class="card-badge">{{ humanizeExecutionRule(s.executionRuleJson) }}</span>
          </div>
          <div class="card-header-actions">
            <span class="card-meta">v{{ s.version }}</span>
          </div>
        </div>

        <div class="card-body">
          <div class="info-row">
            <span class="info-label">入场</span>
            <span class="info-value cond-text">{{ humanizeCondition(s.entryConditionJson) }}</span>
          </div>
          <div class="info-row">
            <span class="info-label">出场</span>
            <span class="info-value cond-text">{{ humanizeCondition(s.exitConditionJson) }}</span>
          </div>
          <div class="info-row">
            <span class="info-label">更新</span>
            <span class="info-value">{{ new Date(s.updatedAt).toLocaleString('zh-CN', { hour12: false }) }}</span>
          </div>
        </div>

        <div class="card-footer">
          <AppButton size="sm" icon="edit" @click="openEdit(s)">编辑</AppButton>
          <AppButton size="sm" variant="danger" icon="trash" @click="remove(s.id)">删除</AppButton>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.strategies-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.page-header h2 { margin: 0; color: var(--text-primary); }
.btn-primary { padding: 0.5rem 1rem; background: var(--accent-blue); color: var(--text-primary); border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: var(--text-primary); border: 1px solid var(--glass-border-strong); border-radius: 4px; cursor: pointer; }
.btn-small { padding: 0.25rem 0.75rem; background: #334155; color: var(--text-primary); border: 1px solid var(--glass-border-strong); border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
.btn-small:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-danger { color: var(--accent-red); border-color: var(--accent-red); background: transparent; }
.empty { text-align: center; color: var(--text-muted); padding: 2rem; }
.loading { text-align: center; color: var(--text-muted); padding: 2rem; }
.card-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(360px, 1fr));
  gap: 1rem;
}

.strategy-card {
  background: var(--card-bg, #fff);
  border: 1px solid var(--glass-border);
  border-top: 3px solid;
  border-radius: 8px;
  overflow: hidden;
  transition: box-shadow 0.2s ease, transform 0.2s ease;
}
.strategy-card:hover {
  box-shadow: 0 8px 28px rgba(0, 0, 0, 0.06);
  transform: translateY(-2px);
}

.card-header {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 1rem 1.25rem 0.5rem;
}

.card-title-area {
  flex: 1;
  min-width: 0;
}
.card-title-area h3 {
  margin: 0 0 0.25rem;
  font-size: 1rem;
  color: var(--text-primary);
  font-weight: 600;
  line-height: 1.3;
}

.card-header-actions {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  align-self: flex-start;
}

.card-badge {
  display: inline-block;
  padding: 0.1rem 0.5rem;
  border-radius: 4px;
  font-size: 0.72rem;
  font-weight: 600;
  line-height: 1.5;
  background: rgba(79, 126, 201, 0.10);
  color: var(--accent-blue);
}

.card-meta {
  font-size: 0.78rem;
  color: var(--text-muted);
}

.card-body {
  padding: 0.5rem 1.25rem 0.75rem;
}

.info-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.3rem 0;
  font-size: 0.85rem;
}

.info-label {
  color: var(--text-muted);
  flex-shrink: 0;
}

.info-value {
  color: var(--text-primary);
  display: flex;
  align-items: center;
  justify-content: flex-end;
  gap: 0.4rem;
}

.cond-text {
  font-size: 0.8rem;
  color: var(--text-muted);
  max-width: 200px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.card-footer {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem 1.25rem 1rem;
  border-top: 1px solid var(--glass-border);
}
.card-footer :deep(.app-button) {
  flex: 1;
}
.input { width: 100%; padding: 0.75rem; margin-bottom: 1rem; border: 1px solid var(--glass-border); border-radius: 4px; background: rgba(255,255,255,0.35); color: var(--text-primary); box-sizing: border-box; font-family: inherit; }
.form-body { max-height: 65vh; overflow-y: auto; margin-bottom: 1rem; }
.section { margin-bottom: 1rem; }
.section-label { display: block; color: var(--text-muted); font-size: 0.85rem; margin-bottom: 0.375rem; font-weight: 600; }
.presets-section { margin-bottom: 1rem; }
.presets-hint { color: var(--text-muted); font-size: 0.85rem; margin: 0 0 0.75rem; }
.preset-cards { display: flex; flex-direction: column; gap: 0.5rem; }
.preset-card {
  display: flex; flex-direction: column; gap: 0.25rem;
  padding: 0.75rem 1rem; border: 1px solid var(--glass-border); border-radius: 6px;
  cursor: pointer; background: rgba(255,255,255,0.35); transition: border-color 0.15s;
}
.preset-card:hover { border-color: var(--accent-blue); background: rgba(79, 126, 201, 0.05); }
.preset-name { color: var(--text-primary); font-size: 0.9rem; }
.preset-desc { color: var(--text-muted); font-size: 0.8rem; line-height: 1.4; }
.notes-section {
  background: rgba(79, 126, 201, 0.06);
  border: 1px solid rgba(79, 126, 201, 0.2);
  border-radius: 6px;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
}
.notes-title {
  display: block;
  color: var(--accent-blue);
  font-size: 0.8rem;
  font-weight: 600;
  margin-bottom: 0.5rem;
}
.notes-list {
  list-style: none;
  padding: 0;
  margin: 0;
}
.notes-list li {
  color: var(--text-muted);
  font-size: 0.8rem;
  line-height: 1.5;
  padding: 0.125rem 0;
}
.notes-list li::before {
  content: '·';
  color: var(--accent-blue);
  font-weight: 700;
  margin-right: 0.5rem;
}
</style>
