<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { Message, Modal } from '@arco-design/web-vue'
import { strategiesApi, type Strategy, type StrategySchema } from '../api/strategies'
import { strategyPresets } from '../api/strategyPresets'
import ChainCanvas from '../components/chain/ChainCanvas.vue'
import { createDefaultChain } from '../components/chain/types'

const schema = ref<StrategySchema | null>(null)
const strategies = ref<Strategy[]>([])
const loading = ref(true)
const showForm = ref(false)
const editId = ref<string | null>(null)
const showPresets = ref(true)
const formName = ref('')
const formChains = ref('')
const saving = ref(false)

/** 初始值快照（用于脏检测） */
const initialName = ref('')
const initialChains = ref('')

const dirty = computed(() =>
  formName.value !== initialName.value || formChains.value !== initialChains.value
)

function captureInitial() {
  initialName.value = formName.value
  initialChains.value = formChains.value
}

function resetForm() {
  formName.value = ''
  formChains.value = JSON.stringify([createDefaultChain()])
}

function openCreate() {
  editId.value = null
  resetForm()
  captureInitial()
  showPresets.value = true
  showForm.value = true
}

function applyPreset(preset: typeof strategyPresets[0]) {
  formName.value = preset.name
  formChains.value = preset.chains
  showPresets.value = false
}

async function openEdit(s: Strategy) {
  editId.value = s.id
  try {
    const { data } = await strategiesApi.getPureById(s.id)
    formName.value = data.name
    formChains.value = data.chainsJson ?? JSON.stringify([createDefaultChain()])
  } catch {
    formName.value = s.name
    formChains.value = JSON.stringify([createDefaultChain()])
  }
  captureInitial()
  showForm.value = true
}

function handleClose(done: () => void) {
  if (dirty.value) {
    Modal.confirm({
      title: '放弃编辑',
      content: '当前编辑尚未保存，确定放弃吗？',
      okText: '确定放弃',
      cancelText: '取消',
      okButtonProps: { status: 'danger' },
      onOk: () => done()
    })
  } else {
    done()
  }
}

async function save() {
  if (!formName.value.trim()) {
    Message.error('请输入策略名称')
    return
  }

  saving.value = true
  try {
    if (editId.value) {
      await strategiesApi.updatePure(editId.value, {
        name: formName.value,
        chains: formChains.value
      })
    } else {
      await strategiesApi.createPure({
        name: formName.value,
        chains: formChains.value
      })
    }
    showForm.value = false
    await load()
    Message.success(`策略「${formName.value}」${editId.value ? '更新' : '创建'}成功`)
  } catch (e: any) {
    const msg = e?.response?.data?.message || e?.message || '保存失败'
    Message.error(msg)
  } finally {
    saving.value = false
  }
}

async function remove(id: string, name: string) {
  Modal.confirm({
    title: '删除策略',
    content: `确定删除策略「${name}」吗？此操作不可撤销。`,
    okText: '确定删除',
    okButtonProps: { status: 'danger' },
    onOk: async () => {
      try {
        await strategiesApi.deletePure(id)
        await load()
      } catch (e: any) {
        const msg = e?.response?.data?.message || e?.message || '删除失败'
        Message.error(msg)
      }
    }
  })
}

function humanizeChains(chainsJson: string): string {
  try {
    const chains = JSON.parse(chainsJson)
    if (!Array.isArray(chains)) return '未配置'
    return chains.map((c: any) => `${c.name} (${c.nodes?.length ?? 0} 节点)`).join('；')
  } catch {
    return '自定义'
  }
}

async function load() {
  loading.value = true
  try {
    // 加载 schema（可选，不存在时降级）
    try {
      const { data: sch } = await strategiesApi.getSchema()
      schema.value = sch
    } catch {
      schema.value = null
    }

    const { data } = await strategiesApi.getAllPure()
    strategies.value = data.data ?? []
  } finally {
    loading.value = false
  }
}

function onBeforeUnload(e: BeforeUnloadEvent) {
  if (dirty.value) {
    e.preventDefault()
  }
}

onMounted(() => {
  load()
  window.addEventListener('beforeunload', onBeforeUnload)
})
</script>

<template>
  <div class="strategies-page">
    <header class="page-header">
      <h2>策略</h2>
      <a-button type="primary" @click="openCreate">
        <template #icon><icon-plus /></template>
        新建策略
      </a-button>
    </header>

    <a-modal
      v-model:visible="showForm"
      :title="editId ? '编辑策略' : '新建策略'"
      width="960px"
      :mask-closable="false"
      @before-close="handleClose"
    >
      <div class="form-body">
        <!-- 预设选择（仅新建时显示） -->
        <div v-if="showPresets && !editId" class="presets-section">
          <p class="presets-hint">选择一个预设模板快速创建，或直接编辑下方规则链</p>
          <div class="preset-grid">
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

        <div class="basic-info">
          <a-input
            v-model="formName"
            placeholder="策略名称"
            size="large"
            :max-length="100"
            show-word-limit
          />
        </div>

        <div class="chain-section" v-if="schema">
          <ChainCanvas
            v-model="formChains"
            :schema="schema"
          />
        </div>
        <div v-else class="chain-section">
          <a-empty description="无法加载策略配置模板，请刷新重试" />
        </div>
      </div>

      <template #footer>
        <a-button @click="showForm = false">取消</a-button>
        <a-button type="primary" :disabled="!formName.trim() || saving" :loading="saving" @click="save">
          {{ editId ? '保存' : '创建策略' }}
        </a-button>
      </template>
    </a-modal>

    <div v-if="loading" class="loading">加载中...</div>
    <div v-else-if="strategies.length === 0" class="empty">暂无策略</div>
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
            <span class="card-badge">{{ humanizeChains(s.chainsJson ?? '[]') }}</span>
          </div>
          <div class="card-header-actions">
            <span class="card-meta">v{{ s.version }}</span>
          </div>
        </div>

        <div class="card-footer">
          <a-button size="small" @click="openEdit(s)">
            <template #icon><icon-edit /></template>
            编辑
          </a-button>
          <a-button size="small" status="danger" @click="remove(s.id, s.name)">
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
.card-footer {
  display: flex;
  gap: 0.5rem;
  padding: 0.75rem 1.25rem 1rem;
  border-top: 1px solid var(--glass-border);
}
.card-footer :deep(.arco-btn) {
  flex: 1;
}
.form-body { display: flex; flex-direction: column; gap: 16px; min-height: 0; flex: 1; }
.basic-info { flex-shrink: 0; }
.chain-section { flex: 1; min-height: 0; display: flex; flex-direction: column; }
.presets-section { }
.presets-hint { color: var(--color-text-3); font-size: 13px; margin: 0 0 8px; }
.preset-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; }
.preset-card {
  display: flex; flex-direction: column; gap: 4px;
  padding: 10px 12px; border: 1px solid var(--color-border-2); border-radius: 8px;
  cursor: pointer; transition: all 0.15s; background: var(--color-bg-1);
}
.preset-card:hover { border-color: rgb(var(--arcoblue-6)); box-shadow: 0 2px 8px rgba(var(--arcoblue-6), 0.08); }
.preset-name { font-size: 14px; font-weight: 600; color: var(--color-text-1); }
.preset-desc { font-size: 12px; color: var(--color-text-3); line-height: 1.4; display: -webkit-box; -webkit-line-clamp: 2; -webkit-box-orient: vertical; overflow: hidden; }
</style>
