<script setup lang="ts">
defineProps<{
  fromPhase?: string
  toPhase?: string
  visible?: boolean
}>()
</script>

<template>
  <div class="phase-arrow" :class="{ hidden: visible === false }">
    <div class="arrow-line" />
    <div class="arrow-head">
      <svg width="12" height="8" viewBox="0 0 12 8" fill="none">
        <path d="M1 1L6 7L11 1" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" />
      </svg>
    </div>
    <div v-if="fromPhase && toPhase" class="arrow-label">
      {{ fromPhase }} → {{ toPhase }}
    </div>
  </div>
</template>

<style scoped>
.phase-arrow {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 2px 0;
  gap: 0;
  color: var(--color-text-4);
  transition: opacity 0.2s;
}
.phase-arrow.hidden {
  opacity: 0;
  pointer-events: none;
  height: 0;
  padding: 0;
  overflow: hidden;
}
.arrow-line {
  width: 1px;
  height: 12px;
  background: var(--color-border-3);
}
.arrow-head {
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--color-text-4);
  margin-top: -2px;
}
.arrow-label {
  font-size: 10px;
  color: var(--color-text-4);
  margin-top: 1px;
  white-space: nowrap;
}
</style>
