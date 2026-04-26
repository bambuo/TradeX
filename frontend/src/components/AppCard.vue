<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  as?: string
  to?: string
  variant?: 'glass' | 'subtle' | 'solid'
  padding?: 'none' | 'sm' | 'md' | 'lg'
  interactive?: boolean
}>(), {
  as: 'div',
  variant: 'glass',
  padding: 'md',
  interactive: false
})

const componentTag = computed(() => props.to ? 'RouterLink' : props.as)
</script>

<template>
  <component
    :is="componentTag"
    :to="to"
    class="app-card"
    :class="[
      `app-card--${variant}`,
      `app-card--pad-${padding}`,
      { 'app-card--interactive': interactive || to }
    ]"
  >
    <slot />
  </component>
</template>

<style scoped>
.app-card {
  position: relative;
  display: block;
  border-radius: 6px;
  color: var(--text-primary);
  text-decoration: none;
}

.app-card--glass {
  background: rgba(255, 255, 255, 0.72);
  border: 1px solid rgba(0, 0, 0, 0.06);
}

.app-card--subtle {
  background: rgba(255, 255, 255, 0.34);
  border: 1px solid rgba(0, 0, 0, 0.04);
}

.app-card--solid {
  background: #f8f3eb;
  border: 1px solid rgba(0, 0, 0, 0.06);
  box-shadow: 0 2px 8px rgba(139, 119, 88, 0.06);
}

.app-card--pad-none { padding: 0; }
.app-card--pad-sm { padding: 0.85rem; }
.app-card--pad-md { padding: 1.1rem; }
.app-card--pad-lg { padding: 1.5rem; }

.app-card--interactive {
  cursor: pointer;
  transition: transform 0.14s ease, box-shadow 0.14s ease, border-color 0.14s ease;
}

.app-card--interactive:hover {
  transform: translateY(-2px);
  box-shadow: 0 6px 20px rgba(139, 119, 88, 0.10);
  border-color: rgba(0, 0, 0, 0.10);
}

.app-card--interactive:active {
  transform: translateY(0);
  box-shadow: none;
}
</style>
