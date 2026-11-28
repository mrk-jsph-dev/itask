<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, useId, useTemplateRef } from 'vue'

defineProps<{
  title: string
  /** Wider, inset, rounded panel for dense content such as card detail. */
  wide?: boolean
}>()
defineSlots<{
  default(): unknown
  /** Replaces the title row; the dialog is then labelled by `title`. */
  header?(): unknown
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
    <div class="fade-in fixed inset-0 z-40 bg-scrim/30" aria-hidden="true" @click="emit('close')" />
    <div
      ref="panel"
      role="dialog"
      aria-modal="true"
      :aria-labelledby="$slots.header ? undefined : titleId"
      :aria-label="$slots.header ? title : undefined"
      :class="
        wide
          ? 'max-w-[760px] md:inset-y-4 md:right-4 md:w-[calc(100%-32px)] md:rounded-[24px] md:shadow-float'
          : 'gap-5 p-6 max-w-md'
      "
      class="sheet fixed inset-y-0 right-0 z-40 flex w-full flex-col overflow-y-auto bg-panel shadow-sheet"
    >
      <slot v-if="$slots.header" name="header" />
      <div v-else class="flex items-center justify-between gap-4" :class="{ 'px-6 pt-6': wide }">
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
