<script setup lang="ts">
withDefaults(defineProps<{
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger' | 'success' | 'warning'
  size?: 'sm' | 'md' | 'lg'
  icon?: string
  type?: 'button' | 'submit' | 'reset'
  disabled?: boolean
  title?: string
}>(), {
  variant: 'secondary',
  size: 'md',
  type: 'button',
  disabled: false
})

const emit = defineEmits<{
  click: [event: MouseEvent]
}>()

function handleClick(event: MouseEvent) {
  emit('click', event)
}
</script>

<template>
  <button
    class="app-button"
    :class="[`app-button--${variant}`, `app-button--${size}`]"
    :type="type"
    :disabled="disabled"
    :title="title"
    @click="handleClick"
  >
    <AppIcon v-if="icon" :name="icon" :size="size === 'sm' ? 14 : 16" />
    <slot />
  </button>
</template>

<style scoped>
.app-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.42rem;
  border: 1px solid transparent;
  border-radius: 10px;
  cursor: pointer;
  color: var(--text-primary);
  font-weight: 650;
  line-height: 1;
  white-space: nowrap;
  transition: transform 0.14s ease, border-color 0.14s ease, background 0.14s ease, box-shadow 0.14s ease, opacity 0.14s ease;
  backdrop-filter: blur(20px) saturate(160%);
  -webkit-backdrop-filter: blur(20px) saturate(160%);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.14), 0 8px 24px rgba(2, 6, 23, 0.16);
}

.app-button:hover:not(:disabled) {
  transform: translateY(-1px);
}

.app-button:active:not(:disabled) {
  transform: translateY(0);
}

.app-button:disabled {
  opacity: 0.48;
  cursor: not-allowed;
  transform: none;
}

.app-button--sm {
  min-height: 2rem;
  padding: 0.38rem 0.72rem;
  font-size: 0.78rem;
  border-radius: 8px;
}

.app-button--md {
  min-height: 2.45rem;
  padding: 0.58rem 1rem;
  font-size: 0.88rem;
}

.app-button--lg {
  min-height: 2.9rem;
  padding: 0.75rem 1.2rem;
  font-size: 0.95rem;
}

.app-button--primary {
  color: #04111f;
  background: linear-gradient(135deg, rgba(125, 211, 252, 0.96), rgba(56, 189, 248, 0.78));
  border-color: rgba(255, 255, 255, 0.34);
}

.app-button--secondary {
  color: var(--text-secondary);
  background: linear-gradient(145deg, rgba(255, 255, 255, 0.11), rgba(255, 255, 255, 0.045));
  border-color: var(--glass-border);
}

.app-button--ghost {
  color: var(--text-muted);
  background: rgba(255, 255, 255, 0.035);
  border-color: rgba(255, 255, 255, 0.12);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.08);
}

.app-button--danger {
  color: #fecaca;
  background: rgba(239, 68, 68, 0.12);
  border-color: rgba(239, 68, 68, 0.44);
}

.app-button--success {
  color: #bbf7d0;
  background: rgba(34, 197, 94, 0.12);
  border-color: rgba(34, 197, 94, 0.42);
}

.app-button--warning {
  color: #fde68a;
  background: rgba(245, 158, 11, 0.12);
  border-color: rgba(245, 158, 11, 0.42);
}
</style>
