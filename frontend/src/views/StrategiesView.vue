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
      <button class="btn-primary" @click="openCreate">新建策略</button>
    </header>

    <div v-if="showForm" class="modal-overlay" @click.self="showForm = false">
      <div class="modal">
        <h3>{{ editId ? '编辑策略模板' : '新建策略模板' }}</h3>

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

        <div class="modal-actions">
          <button class="btn-secondary" @click="showForm = false">取消</button>
          <button class="btn-primary" @click="save">保存</button>
        </div>
      </div>
    </div>

    <div v-if="loading">加载中...</div>
    <table v-else class="table">
      <thead>
        <tr>
          <th>名称</th>
          <th>入场条件</th>
          <th>出场条件</th>
          <th>类型</th>
          <th>版本</th>
          <th>更新时间</th>
          <th>操作</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="s in strategies" :key="s.id">
          <td>{{ s.name }}</td>
          <td class="cond-cell">{{ humanizeCondition(s.entryConditionJson) }}</td>
          <td class="cond-cell">{{ humanizeCondition(s.exitConditionJson) }}</td>
          <td>{{ humanizeExecutionRule(s.executionRuleJson) }}</td>
          <td>{{ s.version }}</td>
          <td>{{ new Date(s.updatedAt_utc).toLocaleString() }}</td>
          <td class="actions">
            <button class="btn-small" @click="openEdit(s)">编辑</button>
            <button class="btn-small btn-danger" @click="remove(s.id)">删除</button>
          </td>
        </tr>
        <tr v-if="strategies.length === 0">
          <td colspan="7" class="empty">暂无策略模板</td>
        </tr>
      </tbody>
    </table>
  </div>
</template>

<style scoped>
.strategies-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.page-header h2 { margin: 0; color: #e2e8f0; }
.btn-primary { padding: 0.5rem 1rem; background: #38bdf8; color: #0f172a; border: none; border-radius: 4px; cursor: pointer; font-weight: 600; }
.btn-secondary { padding: 0.5rem 1rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; }
.btn-small { padding: 0.25rem 0.75rem; background: #334155; color: #e2e8f0; border: 1px solid #475569; border-radius: 4px; cursor: pointer; font-size: 0.8rem; }
.btn-small:disabled { opacity: 0.5; cursor: not-allowed; }
.btn-danger { color: #ef4444; border-color: #ef4444; background: transparent; }
.table { width: 100%; border-collapse: collapse; }
.table th, .table td { padding: 0.75rem; text-align: left; border-bottom: 1px solid #334155; color: #e2e8f0; }
.table th { color: #94a3b8; font-weight: 600; }
.actions { display: flex; gap: 0.5rem; flex-wrap: wrap; }
.cond-cell { font-size: 0.8rem; color: #94a3b8; max-width: 180px; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.empty { text-align: center; color: #64748b; padding: 2rem; }
.modal-overlay { position: fixed; inset: 0; background: rgba(0,0,0,0.5); display: flex; justify-content: center; align-items: center; z-index: 100; }
.modal { background: #1e293b; padding: 2rem; border-radius: 8px; width: 100%; max-width: 720px; max-height: 90vh; overflow-y: auto; }
.modal h3 { margin: 0 0 1rem; color: #e2e8f0; }
.input { width: 100%; padding: 0.75rem; margin-bottom: 1rem; border: 1px solid #334155; border-radius: 4px; background: #0f172a; color: #e2e8f0; box-sizing: border-box; font-family: inherit; }
.form-body { max-height: 65vh; overflow-y: auto; margin-bottom: 1rem; }
.section { margin-bottom: 1rem; }
.section-label { display: block; color: #94a3b8; font-size: 0.85rem; margin-bottom: 0.375rem; font-weight: 600; }
.modal-actions { display: flex; justify-content: flex-end; gap: 0.5rem; }
.presets-section { margin-bottom: 1rem; }
.presets-hint { color: #94a3b8; font-size: 0.85rem; margin: 0 0 0.75rem; }
.preset-cards { display: flex; flex-direction: column; gap: 0.5rem; }
.preset-card {
  display: flex; flex-direction: column; gap: 0.25rem;
  padding: 0.75rem 1rem; border: 1px solid #334155; border-radius: 6px;
  cursor: pointer; background: #0f172a; transition: border-color 0.15s;
}
.preset-card:hover { border-color: #38bdf8; background: rgba(56, 189, 248, 0.05); }
.preset-name { color: #e2e8f0; font-size: 0.9rem; }
.preset-desc { color: #64748b; font-size: 0.8rem; line-height: 1.4; }
.notes-section {
  background: rgba(56, 189, 248, 0.06);
  border: 1px solid rgba(56, 189, 248, 0.2);
  border-radius: 6px;
  padding: 0.75rem 1rem;
  margin-bottom: 1rem;
}
.notes-title {
  display: block;
  color: #38bdf8;
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
  color: #94a3b8;
  font-size: 0.8rem;
  line-height: 1.5;
  padding: 0.125rem 0;
}
.notes-list li::before {
  content: '·';
  color: #38bdf8;
  font-weight: 700;
  margin-right: 0.5rem;
}
</style>
