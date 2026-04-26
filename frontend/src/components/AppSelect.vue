<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'

const props = withDefaults(defineProps<{
  options: { label: string; value: string | number }[]
  modelValue: string | number
  narrow?: boolean
  full?: boolean
  disabled?: boolean
  form?: boolean
}>(), {
  narrow: false,
  full: false,
  disabled: false,
  form: false
})

const emit = defineEmits<{
  'update:modelValue': [value: string | number]
}>()

const open = ref(false)
const triggerRef = ref<HTMLElement | null>(null)

const selectedLabel = computed(() => props.options.find(o => o.value === props.modelValue)?.label ?? props.modelValue)

function toggle() { if (!props.disabled) open.value = !open.value }

function select(value: string | number) {
  if (props.disabled) return
  emit('update:modelValue', value)
  open.value = false
}

function onDocumentClick(e: MouseEvent) {
  if (triggerRef.value && !triggerRef.value.contains(e.target as Node)) {
    open.value = false
  }
}

onMounted(() => document.addEventListener('click', onDocumentClick))
onUnmounted(() => document.removeEventListener('click', onDocumentClick))
</script>

<template>
  <div ref="triggerRef" class="app-select" :class="{ narrow, full, disabled, form }">
    <button class="app-select-trigger" @click="toggle">
      {{ selectedLabel }}
      <svg class="app-select-arrow" :class="{ open }" width="10" height="6" viewBox="0 0 10 6">
        <path d="M1 1l4 4 4-4" stroke="currentColor" stroke-width="1.5" fill="none" stroke-linecap="round" />
      </svg>
    </button>
    <div v-if="open" class="app-select-dropdown">
      <div
        v-for="opt in options"
        :key="opt.value"
        class="app-select-option"
        :class="{ active: opt.value === modelValue }"
        @click="select(opt.value)"
      >
        {{ opt.label }}
      </div>
    </div>
  </div>
</template>

<style scoped>
.app-select {
  position: relative;
  display: inline-block;
}

.app-select-trigger {
  display: inline-flex;
  align-items: center;
  gap: 0.4rem;
  padding: 4px 6px;
  background: rgba(255,255,255,0.35);
  color: var(--text-primary);
  border: 1px solid var(--glass-border);
  border-radius: 4px;
  font-size: 0.8rem;
  cursor: pointer;
  white-space: nowrap;
  min-width: 50px;
  font-family: inherit;
}

.app-select-trigger:hover {
  border-color: #475569;
}

.app-select-arrow {
  flex-shrink: 0;
  margin-left: auto;
  transition: transform 0.15s ease;
  color: var(--text-muted);
}
.app-select-arrow.open {
  transform: rotate(180deg);
}

.app-select-dropdown {
  position: absolute;
  top: calc(100% + 4px);
  left: 0;
  z-index: 20;
  min-width: 100%;
  background: #fff;
  border: 1px solid var(--glass-border);
  border-radius: 6px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.08);
  overflow: hidden;
}

.app-select-option {
  padding: 0.5rem 0.75rem;
  color: var(--text-primary);
  font-size: 0.8rem;
  cursor: pointer;
  white-space: nowrap;
  transition: background 0.1s ease;
}

.app-select-option:hover {
  background: rgba(79, 126, 201, 0.06);
}

.app-select-option.active {
  background: rgba(79, 126, 201, 0.1);
  color: var(--accent-blue);
  font-weight: 600;
}

.disabled { opacity: 0.45; pointer-events: none; }
.full { display: block; }
.full .app-select-trigger { width: 100%; box-sizing: border-box; }
.full .app-select-dropdown { width: 100%; box-sizing: border-box; }

.narrow .app-select-trigger {
  width: 100px;
}

.narrow .app-select-dropdown {
  min-width: 100px;
}

.form .app-select-trigger {
  min-height: 2.45rem;
  padding: 0.5rem 0.75rem;
  font-size: 0.85rem;
  box-sizing: border-box;
}
</style>
