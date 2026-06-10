<script setup lang="ts">
import { computed } from 'vue'
import type { StrategySchema } from '../api/strategies'
import ConditionTreeEditor, { type ConditionNode } from './ConditionTreeEditor.vue'

export interface RuleAction {
  action: 'buy' | 'sell' | 'sellAll' | 'hold'
  size?: number
  sizeType?: 'fixed' | 'multiplier'
  sizeMultiplierRef?: string
  reason?: string
}

export interface RuleConstraints {
  maxPositions?: number
  maxPositionValue?: number
  minInterval?: number
}

export interface TradingRule {
  code: string
  name: string
  when: ConditionNode
  then: RuleAction
  context?: 'any' | 'noPosition' | 'hasPosition'
  priority?: number
  constraints?: RuleConstraints
}

export interface RuleSet {
  code: string
  name: string
  rules: TradingRule[]
  params?: Record<string, number>
}

const props = defineProps<{
  modelValue: string
  schema?: StrategySchema
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const ruleSet = computed({
  get: (): RuleSet => {
    try {
      const parsed = JSON.parse(props.modelValue)
      if (parsed.rules) return parsed as RuleSet
      return { code: '', name: '', rules: [] }
    } catch {
      return { code: '', name: '', rules: [] }
    }
  },
  set: (val: RuleSet) => emit('update:modelValue', JSON.stringify(val, null, 2))
})

const actionLabels: Record<string, string> = {
  buy: '买入', sell: '减仓', sellAll: '全平', hold: '保持'
}

const contextLabels: Record<string, string> = {
  any: '不限', noPosition: '无持仓', hasPosition: '有持仓'
}

function addRule() {
  const idx = ruleSet.value.rules.length + 1
  ruleSet.value = {
    ...ruleSet.value,
    rules: [
      ...ruleSet.value.rules,
      {
        code: `rule_${idx}`,
        name: `规则 ${idx}`,
        when: { operator: 'AND', conditions: [] },
        then: { action: 'buy', size: 100, sizeType: 'fixed', reason: '' },
        context: 'any',
        priority: 0
      }
    ]
  }
}

function removeRule(index: number) {
  const updated = { ...ruleSet.value }
  updated.rules = [...updated.rules]
  updated.rules.splice(index, 1)
  ruleSet.value = updated
}

function updateRule(index: number, field: string, value: unknown) {
  const updated = { ...ruleSet.value }
  updated.rules = [...updated.rules]
  updated.rules[index] = { ...updated.rules[index], [field]: value }
  ruleSet.value = updated
}

function updateAction(index: number, field: string, value: unknown) {
  const updated = { ...ruleSet.value }
  updated.rules = [...updated.rules]
  updated.rules[index] = {
    ...updated.rules[index],
    then: { ...updated.rules[index].then, [field]: value }
  }
  ruleSet.value = updated
}

function updateConstraints(index: number, field: keyof RuleConstraints, value: number | undefined) {
  const rule = { ...ruleSet.value.rules[index] }
  if (value === undefined || value === null || value === 0) {
    const old = rule.constraints || {} as RuleConstraints
    const next: Record<string, number> = {}
    for (const k of Object.keys(old) as (keyof RuleConstraints)[]) {
      if (k !== field) next[k] = old[k]!
    }
    rule.constraints = Object.keys(next).length > 0 ? next as RuleConstraints : undefined
  } else {
    rule.constraints = { ...rule.constraints, [field]: value }
  }
  updateRule(index, 'constraints', rule.constraints)
}

function updateWhen(index: number, node: ConditionNode | null) {
  if (node) {
    updateRule(index, 'when', node)
  }
}

function setTopField(field: string, value: unknown) {
  ruleSet.value = { ...ruleSet.value, [field]: value }
}
</script>

<template>
  <div class="ruleset-editor">
    <!-- 策略元信息 -->
    <div class="meta-grid">
      <div class="meta-item">
        <label class="field-label">策略代码</label>
        <a-input
          :model-value="ruleSet.code"
          placeholder="如: my_strategy"
          @change="(v: unknown) => setTopField('code', String(v))"
        />
      </div>
      <div class="meta-item">
        <label class="field-label">策略名称</label>
        <a-input
          :model-value="ruleSet.name"
          placeholder="策略名称"
          @change="(v: unknown) => setTopField('name', String(v))"
        />
      </div>
    </div>

    <!-- 规则列表 -->
    <div class="rules-section">
      <div class="section-header">
        <span class="section-title">规则 ({{ ruleSet.rules.length }})</span>
        <a-button size="mini" type="outline" @click="addRule">
          <template #icon><icon-plus /></template>
          添加规则
        </a-button>
      </div>

      <div v-if="ruleSet.rules.length === 0" class="rules-empty">
        暂无规则，点击"添加规则"开始配置
      </div>

      <div v-for="(rule, i) in ruleSet.rules" :key="i" class="rule-card">
        <div class="rule-header">
          <span class="rule-index">#{{ i + 1 }}</span>
          <a-input
            :model-value="rule.name"
            size="small"
            placeholder="规则名称"
            style="width: 160px"
            @change="(v: unknown) => updateRule(i, 'name', String(v))"
          />
          <a-input
            :model-value="rule.code"
            size="small"
            placeholder="规则代码"
            style="width: 140px"
            @change="(v: unknown) => updateRule(i, 'code', String(v))"
          />
          <a-button size="mini" type="text" status="danger" @click="removeRule(i)">
            <template #icon><icon-delete /></template>
          </a-button>
        </div>

        <div class="rule-config-grid">
          <!-- 上下文 -->
          <div class="config-item">
            <label class="field-label">上下文</label>
            <a-select
              :model-value="rule.context ?? 'any'"
              size="small"
              @change="(v: unknown) => updateRule(i, 'context', String(v))"
            >
              <a-option v-for="(label, key) in contextLabels" :key="key" :value="key" :label="label" />
            </a-select>
          </div>

          <!-- 优先级 -->
          <div class="config-item">
            <label class="field-label">优先级</label>
            <a-input-number
              :model-value="rule.priority ?? 0"
              size="small"
              :min="0"
              style="width: 100%"
              @change="(v: unknown) => updateRule(i, 'priority', Number(v) || 0)"
            />
          </div>

          <!-- 约束 -->
          <div class="config-item">
            <label class="field-label">最大持仓数</label>
            <a-input-number
              :model-value="rule.constraints?.maxPositions"
              size="small"
              :min="0"
              placeholder="不限"
              style="width: 100%"
              @change="(v: unknown) => updateConstraints(i, 'maxPositions', v !== undefined && v !== null ? Number(v) : undefined)"
            />
          </div>
          <div class="config-item">
            <label class="field-label">最大持仓价值 ($)</label>
            <a-input-number
              :model-value="rule.constraints?.maxPositionValue"
              size="small"
              :min="0"
              placeholder="不限"
              style="width: 100%"
              @change="(v: unknown) => updateConstraints(i, 'maxPositionValue', v !== undefined && v !== null ? Number(v) : undefined)"
            />
          </div>
          <div class="config-item">
            <label class="field-label">最小间隔 (秒)</label>
            <a-input-number
              :model-value="rule.constraints?.minInterval"
              size="small"
              :min="0"
              placeholder="不限"
              style="width: 100%"
              @change="(v: unknown) => updateConstraints(i, 'minInterval', v !== undefined && v !== null && Number(v) > 0 ? Number(v) : undefined)"
            />
          </div>
        </div>

        <!-- When 条件 -->
        <div class="rule-section">
          <label class="field-label">当 (when) — 触发条件</label>
          <ConditionTreeEditor
            :node="rule.when"
            :schema="schema"
            @update="(n: ConditionNode | null) => updateWhen(i, n)"
          />
        </div>

        <!-- Then 动作 -->
        <div class="rule-section">
          <label class="field-label">则 (then) — 执行动作</label>
          <div class="action-editor">
            <div class="action-row">
              <a-select
                :model-value="rule.then.action"
                size="small"
                style="width: 120px"
                @change="(v: unknown) => updateAction(i, 'action', String(v))"
              >
                <a-option v-for="(label, key) in actionLabels" :key="key" :value="key" :label="label" />
              </a-select>

              <template v-if="rule.then.action === 'buy' || rule.then.action === 'sell'">
                <a-input-number
                  :model-value="rule.then.size"
                  size="small"
                  :min="0"
                  placeholder="数量 ($)"
                  style="width: 120px"
                  @change="(v: unknown) => updateAction(i, 'size', Number(v) || 0)"
                />
                <a-select
                  :model-value="rule.then.sizeType ?? 'fixed'"
                  size="small"
                  style="width: 130px"
                  @change="(v: unknown) => updateAction(i, 'sizeType', String(v))"
                >
                  <a-option value="fixed" label="固定金额 ($)" />
                  <a-option value="multiplier" label="倍率引用" />
                </a-select>
                <a-input
                  v-if="rule.then.sizeType === 'multiplier'"
                  :model-value="rule.then.sizeMultiplierRef"
                  size="small"
                  placeholder="引用指标名"
                  style="width: 150px"
                  @change="(v: unknown) => updateAction(i, 'sizeMultiplierRef', String(v))"
                />
              </template>
            </div>

            <div class="action-row" style="margin-top: 0.375rem">
              <a-input
                :model-value="rule.then.reason"
                size="small"
                placeholder="执行原因（支持 {INDICATOR} 模板变量）"
                style="width: 100%"
                @change="(v: unknown) => updateAction(i, 'reason', String(v))"
              />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 原始 JSON 查看 -->
    <details class="raw-toggle">
      <summary>查看/编辑原始 JSON</summary>
      <textarea
        :value="props.modelValue"
        class="raw-input"
        rows="8"
        @input="(e: Event) => emit('update:modelValue', (e.target as HTMLTextAreaElement).value)"
      ></textarea>
    </details>
  </div>
</template>

<style scoped>
.ruleset-editor {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.meta-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 0.75rem;
}

.meta-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.field-label {
  color: var(--text-muted);
  font-size: 0.8rem;
  font-weight: 600;
}

.rules-section {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.section-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

.section-title {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--text-primary);
}

.rules-empty {
  text-align: center;
  color: var(--text-muted);
  font-size: 0.85rem;
  padding: 1.5rem;
  border: 1px dashed var(--glass-border);
  border-radius: 6px;
}

.rule-card {
  border: 1px solid var(--glass-border);
  border-radius: 8px;
  padding: 0.75rem;
  background: rgba(255, 255, 255, 0.4);
}

.rule-header {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
  flex-wrap: wrap;
}

.rule-index {
  font-weight: 700;
  font-size: 0.8rem;
  color: var(--accent-blue);
  background: rgba(79, 126, 201, 0.1);
  padding: 0.125rem 0.375rem;
  border-radius: 4px;
}

.rule-config-grid {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr 1fr 1fr;
  gap: 0.5rem;
  margin-bottom: 0.75rem;
}

.config-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.rule-section {
  margin-bottom: 0.75rem;
}

.action-editor {
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  padding: 0.5rem;
  background: rgba(255, 255, 255, 0.3);
}

.action-row {
  display: flex;
  gap: 0.375rem;
  align-items: center;
  flex-wrap: wrap;
}

.raw-toggle {
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  padding: 0.5rem;
  color: var(--text-muted);
  font-size: 0.8rem;
}

.raw-toggle summary {
  cursor: pointer;
  padding: 0.25rem 0;
}

.raw-input {
  width: 100%;
  padding: 0.5rem;
  background: rgba(255, 255, 255, 0.35);
  color: var(--text-primary);
  border: 1px solid var(--glass-border);
  border-radius: 4px;
  font-family: monospace;
  font-size: 0.8rem;
  box-sizing: border-box;
  margin-top: 0.5rem;
}
</style>
