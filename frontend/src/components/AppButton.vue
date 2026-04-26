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
  gap: 0.4rem;
  border: 1px solid;
  border-radius: 6px;
  cursor: pointer;
  color: var(--text-primary);
  font-weight: 600;
  line-height: 1;
  white-space: nowrap;
  transition: transform 0.12s ease, box-shadow 0.12s ease, background 0.12s ease, border-color 0.12s ease, opacity 0.12s ease;
}

.app-button:hover:not(:disabled) {
  box-shadow: 0 4px 12px rgba(139, 119, 88, 0.10);
  transform: translateY(-1px);
}

.app-button:active:not(:disabled) {
  box-shadow: none;
  transform: translateY(0);
}

.app-button:disabled {
  opacity: 0.40;
  cursor: not-allowed;
  transform: none;
  box-shadow: none;
}

.app-button--sm {
  min-height: 2rem;
  padding: 0.38rem 0.72rem;
  font-size: 0.78rem;
  border-radius: 6px;
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
  color: #fff;
  background: #4f7ec9;
  border-color: #3d6ab5;
}

.app-button--primary:hover:not(:disabled) {
  background: #5c8ad4;
}

.app-button--secondary {
  color: var(--text-secondary);
  background: rgba(255, 255, 255, 0.82);
  border-color: rgba(0, 0, 0, 0.08);
}

.app-button--secondary:hover:not(:disabled) {
  background: #fff;
  border-color: rgba(0, 0, 0, 0.14);
}

.app-button--ghost {
  color: var(--text-secondary);
  background: transparent;
  border-color: transparent;
}

.app-button--ghost:hover:not(:disabled) {
  color: var(--text-primary);
  background: rgba(0, 0, 0, 0.03);
}

.app-button--danger {
  color: #bf4848;
  background: rgba(191, 72, 72, 0.06);
  border-color: rgba(191, 72, 72, 0.18);
}

.app-button--danger:hover:not(:disabled) {
  color: #fff;
  background: #bf4848;
  border-color: #bf4848;
}

.app-button--success {
  color: #528a60;
  background: rgba(82, 138, 96, 0.06);
  border-color: rgba(82, 138, 96, 0.18);
}

.app-button--success:hover:not(:disabled) {
  color: #fff;
  background: #528a60;
  border-color: #528a60;
}

.app-button--warning {
  color: #b8893a;
  background: rgba(184, 137, 58, 0.06);
  border-color: rgba(184, 137, 58, 0.18);
}

.app-button--warning:hover:not(:disabled) {
  color: #fff;
  background: #b8893a;
  border-color: #b8893a;
}
</style>
