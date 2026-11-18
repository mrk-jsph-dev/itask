<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, useId, useTemplateRef } from 'vue'

defineProps<{
  title: string
  /** Wider panel for dense content such as card detail. */
  wide?: boolean
}>()
const emit = defineEmits<{ close: [] }>()

const titleId = useId()
const panel = useTemplateRef('panel')
let returnFocus: HTMLElement | null = null

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') emit('close')
}

onMounted(async () => {
  returnFocus = document.activeElement as HTMLElement | null
  document.addEventListener('keydown', onKeydown)
  await nextTick()
  const target =
    panel.value?.querySelector<HTMLElement>('input, select, textarea') ??
    panel.value?.querySelector<HTMLElement>('button')
  target?.focus()
})
onBeforeUnmount(() => {
  document.removeEventListener('keydown', onKeydown)
  returnFocus?.focus()
})
</script>

<template>
  <Teleport to="body">
    <div class="fade-in fixed inset-0 z-40 bg-ink/30" aria-hidden="true" @click="emit('close')" />
    <div
      ref="panel"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="titleId"
      :class="wide ? 'max-w-xl' : 'max-w-md'"
      class="sheet fixed inset-y-0 right-0 z-40 flex w-full flex-col gap-5 overflow-y-auto bg-panel p-6 shadow-[0_10px_40px_rgb(17_24_39/0.2)]"
    >
      <div class="flex items-center justify-between gap-4">
        <h2 :id="titleId" class="text-lg font-bold tracking-tight">{{ title }}</h2>
        <button
          type="button"
          class="ghost flex h-9 w-9 items-center justify-center rounded-full text-muted"
          aria-label="Close"
          @click="emit('close')"
        >
          ✕
        </button>
      </div>
      <slot />
    </div>
  </Teleport>
</template>
