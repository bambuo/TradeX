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
  border-radius: 20px;
  color: var(--text-primary);
  text-decoration: none;
  overflow: hidden;
}

.app-card::before {
  content: '';
  position: absolute;
  inset: 0;
  border-radius: inherit;
  pointer-events: none;
  background: linear-gradient(120deg, rgba(255, 255, 255, 0.20), transparent 26%, transparent 74%, rgba(255, 255, 255, 0.05));
  opacity: 0.72;
}

.app-card::after {
  content: '';
  position: absolute;
  inset: 1px;
  border-radius: calc(20px - 1px);
  pointer-events: none;
  border: 1px solid rgba(255, 255, 255, 0.06);
}

.app-card > :deep(*) {
  position: relative;
}

.app-card--glass {
  background:
    linear-gradient(145deg, rgba(255, 255, 255, 0.125), rgba(255, 255, 255, 0.03) 44%, rgba(255, 255, 255, 0.04)),
    rgba(255, 255, 255, 0.035);
  border: 1px solid var(--glass-border);
  box-shadow: inset 0 1px 0 var(--glass-highlight), 0 14px 42px rgba(2, 6, 23, 0.15);
  backdrop-filter: blur(34px) saturate(180%) brightness(1.06);
  -webkit-backdrop-filter: blur(34px) saturate(180%) brightness(1.06);
}

.app-card--subtle {
  background: rgba(255, 255, 255, 0.046);
  border: 1px solid var(--glass-border);
  box-shadow: inset 0 1px 0 rgba(255, 255, 255, 0.16), 0 10px 30px rgba(2, 6, 23, 0.10);
  backdrop-filter: blur(24px) saturate(160%);
  -webkit-backdrop-filter: blur(24px) saturate(160%);
}

.app-card--solid {
  background: rgba(15, 23, 42, 0.62);
  border: 1px solid rgba(255, 255, 255, 0.12);
  box-shadow: 0 12px 34px rgba(2, 6, 23, 0.22);
}

.app-card--pad-none { padding: 0; }
.app-card--pad-sm { padding: 0.85rem; }
.app-card--pad-md { padding: 1.1rem; }
.app-card--pad-lg { padding: 1.5rem; }

.app-card--interactive {
  cursor: pointer;
  transition: transform 0.16s ease, border-color 0.16s ease, box-shadow 0.16s ease, background 0.16s ease;
}

.app-card--interactive:hover {
  transform: translateY(-2px);
  border-color: rgba(56, 189, 248, 0.55);
  box-shadow: inset 0 1px 0 var(--glass-highlight), 0 18px 52px rgba(2, 6, 23, 0.18);
}

.app-card--interactive:active {
  transform: translateY(0);
}
</style>
