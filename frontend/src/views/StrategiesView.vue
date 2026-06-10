<script setup lang="ts">
import { ref, onMounted, computed } from 'vue'
import { Message } from '@arco-design/web-vue'
import { strategiesApi, type Strategy, type StrategySchema } from '../api/strategies'
import { strategyPresets } from '../api/strategyPresets'
import RuleSetEditor from '../components/RuleSetEditor.vue'

const cmpLabels: Record<string, string> = {
  '>': '>', '<': '<', '>=': '≥', '<=': '≤', '==': '＝', CA: '↗', CB: '↘'
}

/** 从 RuleSet JSON 中提取可读的描述 */
function humanizeRuleSet(json: string): string {
  try {
    const parsed = JSON.parse(json)
    if (!parsed.rules || !Array.isArray(parsed.rules)) return '未配置'
    const parts = parsed.rules.map((r: any) => {
      const name = r.name || r.code || '规则'
      const action = r.then?.action || 'hold'
      const actionLabel = action === 'buy' ? '→ 买入' : action === 'sell' ? '→ 减仓' : action === 'sellAll' ? '→ 全平' : ''
      if (r.when && r.when.indicator) {
        const cmp = cmpLabels[r.when.comparison ?? ''] || r.when.comparison || '?'
        return `${name}: ${r.when.indicator} ${cmp} ${r.when.value} ${actionLabel}`
      }
      if (r.when && r.when.conditions && r.when.conditions.length > 0) {
        const condText = r.when.conditions.map((c: any) => {
          if (c.indicator) return `${c.indicator} ${cmpLabels[c.comparison ?? ''] || c.comparison || '?'} ${c.value}`
          return '多条件'
        }).join(r.when.operator === 'OR' ? ' 或 ' : ' 且 ')
        return `${name}: ${condText} ${actionLabel}`
      }
      return `${name} ${actionLabel}`
    })
    return parts.join('；')
  } catch { return json }
}

function humanizeExecutionRule(json: string): string {
  try {
    const obj = JSON.parse(json)
    const ruleCount = obj.rules?.length ?? 0
    const actions = new Set((obj.rules ?? []).map((r: any) => r.then?.action))
    const labels: string[] = []
    if (actions.has('buy')) labels.push('入场')
    if (actions.has('sell') || actions.has('sellAll')) labels.push('出场')
    return `${ruleCount} 条规则${labels.length ? ` (${labels.join('/')})` : ''}`
  } catch { return '自定义' }
}

const schema = ref<StrategySchema | null>(null)
const strategies = ref<Strategy[]>([])
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const showPresets = ref(false)

const activePreset = ref<typeof strategyPresets[0] | null>(null)
const formName = ref('')
const formExecutionRule = ref('{}')

function resetForm() {
  activePreset.value = null
  formName.value = ''
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
  formExecutionRule.value = s.executionRule
  showPresets.value = false
  showForm.value = true
}

function applyPreset(preset: typeof strategyPresets[0]) {
  formName.value = preset.name
  formExecutionRule.value = preset.executionRule
  activePreset.value = preset
  showPresets.value = false
}

async function save() {
  if (!formName.value.trim()) {
    Message.error('请输入策略名称')
    return
  }

  try {
    if (editId.value) {
      await strategiesApi.updatePure(editId.value, {
        name: formName.value,
        executionRule: formExecutionRule.value
      })
    } else {
      await strategiesApi.createPure({
        name: formName.value,
        executionRule: formExecutionRule.value
      })
    }
    showForm.value = false
    await load()
  } catch (e: any) {
    const msg = e?.response?.data?.error || e?.message || '保存失败'
    Message.error(msg)
  }
}

async function remove(id: string) {
  try {
    await strategiesApi.deletePure(id)
    await load()
  } catch (e: any) {
    const msg = e?.response?.data?.error || e?.message || '删除失败'
    Message.error(msg)
  }
}

async function load() {
  loading.value = true
  try {
    const { data: sch } = await strategiesApi.getSchema()
    schema.value = sch

    const { data } = await strategiesApi.getAllPure()
    strategies.value = data.data ?? []
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div class="strategies-page">
    <header class="page-header">
      <h2>策略模板</h2>
      <a-button type="primary" @click="openCreate">
        <template #icon><icon-plus /></template>
        新建策略
      </a-button>
    </header>

    <a-modal v-model:visible="showForm" :title="editId ? '编辑策略模板' : '新建策略模板'" width="1100px" :mask-closable="false">
      <div v-if="showPresets && !editId" class="presets-section">
        <p class="presets-hint">选择一个预设模板快速创建，或直接编辑下方规则集</p>
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

        <a-input v-model="formName" placeholder="策略名称" />

        <div class="section">
          <span class="section-label">规则集 (RuleSet)</span>
          <RuleSetEditor v-model="formExecutionRule" :schema="schema || undefined" />
        </div>
      </div>

      <template #footer>
        <a-button type="primary" @click="save">
          <template #icon><icon-save /></template>
          保存
        </a-button>
      </template>
    </a-modal>

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
            <span class="card-badge">{{ humanizeExecutionRule(s.executionRule) }}</span>
          </div>
          <div class="card-header-actions">
            <span class="card-meta">v{{ s.version }}</span>
          </div>
        </div>

        <div class="card-body">
          <div class="info-row">
            <span class="info-label">规则</span>
            <span class="info-value cond-text">{{ humanizeRuleSet(s.executionRule) }}</span>
          </div>
          <div class="info-row">
            <span class="info-label">更新</span>
            <span class="info-value">{{ new Date(s.updatedAt).toLocaleString('zh-CN', { hour12: false }) }}</span>
          </div>
        </div>

        <div class="card-footer">
          <a-button size="small" @click="openEdit(s)">
            <template #icon><icon-edit /></template>
            编辑
          </a-button>
          <a-button size="small" status="danger" @click="remove(s.id)">
            <template #icon><icon-delete /></template>
            删除
          </a-button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.strategies-page { padding: 2rem; }
.page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 1rem; }
.page-header h2 { margin: 0; color: var(--text-primary); }
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
.card-footer :deep(.arco-btn) {
  flex: 1;
}
.form-body { max-height: 65vh; overflow-y: auto; margin-bottom: 1rem; }
.section { margin-bottom: 1rem; }
.section-label { display: block; color: var(--text-muted); font-size: 0.85rem; margin-bottom: 0.375rem; font-weight: 600; }
.presets-section { margin-bottom: 1rem; }
.presets-hint { color: var(--text-muted); font-size: 0.85rem; margin: 0 0 0.75rem; }
.preset-cards { display: grid; grid-template-columns: 1fr 1fr; gap: 0.5rem; }
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
