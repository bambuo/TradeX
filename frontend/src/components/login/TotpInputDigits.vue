<script setup lang="ts">
import { ref, watch, nextTick, type ComponentPublicInstance } from 'vue'

const props = defineProps<{
  modelValue: string
  error?: string
}>()

const emit = defineEmits<{
  'update:modelValue': [value: string]
  submit: []
}>()

const digits = ref<string[]>(['', '', '', '', '', ''])
const inputEls = ref<(HTMLInputElement | null)[]>([])

function setRef(el: Element | ComponentPublicInstance | null, i: number) {
  inputEls.value[i] = el instanceof HTMLInputElement ? el : null
}

watch(() => props.modelValue, (val) => {
  if (val.length === 0) {
    digits.value = ['', '', '', '', '', '']
  }
})

function handleInput(i: number, e: Event) {
  const target = e.target as HTMLInputElement
  const val = target.value.replace(/\D/g, '')
  target.value = val.slice(-1)
  digits.value[i] = target.value

  if (target.value && i < 5) {
    inputEls.value[i + 1]?.focus()
  }

  const code = digits.value.join('')
  emit('update:modelValue', code)

  if (code.length === 6) {
    nextTick(() => emit('submit'))
  }
}

function handleKeydown(i: number, e: KeyboardEvent) {
  if (e.key === 'Backspace' && !digits.value[i] && i > 0) {
    inputEls.value[i - 1]?.focus()
  }
}

function handlePaste(e: ClipboardEvent) {
  e.preventDefault()
  const text = e.clipboardData?.getData('text')?.replace(/\D/g, '').slice(0, 6) || ''
  for (let i = 0; i < 6; i++) {
    digits.value[i] = text[i] || ''
    if (inputEls.value[i]) inputEls.value[i]!.value = text[i] || ''
  }
  emit('update:modelValue', digits.value.join(''))
  if (text.length === 6) {
    nextTick(() => emit('submit'))
  }
}
</script>

<template>
  <div class="totp-digits">
    <input
      v-for="(_, i) in 6"
      :key="i"
      :ref="(el) => setRef(el, i)"
      :value="digits[i]"
      type="text"
      inputmode="numeric"
      maxlength="1"
      class="digit-box"
      :class="{ filled: digits[i] !== '', error: !!error }"
      @input="handleInput(i, $event)"
      @keydown="handleKeydown(i, $event)"
      @paste="i === 0 && handlePaste($event)"
    />
  </div>
</template>

<style scoped>
.totp-digits {
  display: flex;
  gap: 10px;
  justify-content: center;
}

.digit-box {
  width: 48px;
  height: 56px;
  text-align: center;
  font-size: 24px;
  font-weight: 600;
  font-family: "SF Mono", Menlo, Monaco, Consolas, monospace;
  border: 2px solid var(--color-border-2, #e5e8ef);
  border-radius: 8px;
  outline: none;
  background: var(--color-bg-1, #fff);
  color: var(--color-text-1, #1d2129);
  transition: all 0.15s ease;
}

.digit-box:focus {
  border-color: rgb(22, 93, 255);
  box-shadow: 0 0 0 3px rgba(22, 93, 255, 0.15);
}

.digit-box.filled {
  border-color: var(--color-border-3, #c9cdd4);
  background: var(--color-bg-2, #f2f3f5);
}

.digit-box.error {
  border-color: #f87171;
}

@media (max-width: 480px) {
  .totp-digits {
    gap: 6px;
  }
  .digit-box {
    width: 42px;
    height: 50px;
    font-size: 20px;
  }
}
</style>
