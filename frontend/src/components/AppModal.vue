<script setup lang="ts">
import { computed, onBeforeUnmount, watch } from 'vue'

const props = withDefaults(defineProps<{
  modelValue: boolean
  title?: string
  width?: 'sm' | 'md' | 'lg' | 'xl'
  closeOnBackdrop?: boolean
  showClose?: boolean
}>(), {
  width: 'md',
  closeOnBackdrop: true,
  showClose: true
})

const emit = defineEmits<{
  'update:modelValue': [value: boolean]
  close: []
}>()

const widthClass = computed(() => `app-modal__panel--${props.width}`)

function close() {
  emit('update:modelValue', false)
  emit('close')
}

function onBackdrop() {
  if (props.closeOnBackdrop) close()
}

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape' && props.modelValue) close()
}

watch(() => props.modelValue, (open) => {
  document.body.style.overflow = open ? 'hidden' : ''
})

window.addEventListener('keydown', onKeydown)

onBeforeUnmount(() => {
  window.removeEventListener('keydown', onKeydown)
  document.body.style.overflow = ''
})
</script>

<template>
  <Teleport to="body">
    <Transition name="app-modal-fade">
      <div v-if="modelValue" class="app-modal" @click.self="onBackdrop">
        <section class="app-modal__panel" :class="widthClass" role="dialog" aria-modal="true">
          <header v-if="title || showClose || $slots.header" class="app-modal__header">
            <slot name="header">
              <h3>{{ title }}</h3>
            </slot>
            <button v-if="showClose" class="app-modal__close" type="button" aria-label="关闭" @click="close">
              <AppIcon name="close" />
            </button>
          </header>

          <div class="app-modal__body">
            <slot />
          </div>

          <footer v-if="$slots.footer" class="app-modal__footer">
            <slot name="footer" />
          </footer>
        </section>
      </div>
    </Transition>
  </Teleport>
</template>

<style scoped>
.app-modal {
  position: fixed;
  inset: 0;
  z-index: 1000;
  display: flex;
  align-items: center;
  justify-content: center;
  padding: 1.25rem;
  background:
    radial-gradient(circle at 30% 20%, rgba(56, 189, 248, 0.12), transparent 24rem),
    rgba(2, 6, 23, 0.48);
  backdrop-filter: blur(12px);
  -webkit-backdrop-filter: blur(12px);
}

.app-modal__panel {
  width: 100%;
  max-height: min(88vh, 920px);
  display: flex;
  flex-direction: column;
  border-radius: 24px;
  background:
    linear-gradient(145deg, rgba(255, 255, 255, 0.14), rgba(255, 255, 255, 0.04) 42%, rgba(255, 255, 255, 0.05)),
    rgba(15, 23, 42, 0.52);
  border: 1px solid var(--glass-border);
  box-shadow: 0 32px 100px rgba(2, 6, 23, 0.46), inset 0 1px 0 var(--glass-highlight);
  backdrop-filter: blur(36px) saturate(180%) brightness(1.06);
  -webkit-backdrop-filter: blur(36px) saturate(180%) brightness(1.06);
  overflow: hidden;
}

.app-modal__panel--sm { max-width: 420px; }
.app-modal__panel--md { max-width: 520px; }
.app-modal__panel--lg { max-width: 760px; }
.app-modal__panel--xl { max-width: 1100px; }

.app-modal__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 1rem;
  padding: 1.15rem 1.25rem;
  border-bottom: 1px solid var(--glass-border);
}

.app-modal__header h3 {
  margin: 0;
  color: var(--text-primary);
  font-size: 1.05rem;
}

.app-modal__close {
  width: 2.1rem;
  height: 2.1rem;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border-radius: 999px;
  border: 1px solid rgba(255, 255, 255, 0.14);
  background: rgba(255, 255, 255, 0.06);
  color: var(--text-muted);
  cursor: pointer;
}

.app-modal__close:hover {
  color: var(--text-primary);
  background: rgba(255, 255, 255, 0.10);
}

.app-modal__body {
  padding: 1.25rem;
  overflow: auto;
}

.app-modal__footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.6rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--glass-border);
}

.app-modal-fade-enter-active,
.app-modal-fade-leave-active {
  transition: opacity 0.16s ease;
}

.app-modal-fade-enter-from,
.app-modal-fade-leave-to {
  opacity: 0;
}

@media (max-width: 760px) {
  .app-modal { padding: 0.75rem; align-items: flex-end; }
  .app-modal__panel { max-height: 92vh; border-radius: 22px; }
}
</style>
