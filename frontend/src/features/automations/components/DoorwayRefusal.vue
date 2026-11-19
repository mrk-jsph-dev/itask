<script setup lang="ts">
import { onBeforeUnmount, onMounted, useTemplateRef } from 'vue'

import type { DoorwayRefusal } from '../store'

defineProps<{ refusal: DoorwayRefusal; canOverride: boolean }>()
const emit = defineEmits<{ openCard: []; moveAnyway: []; close: [] }>()

const panel = useTemplateRef('panel')

function onKeydown(event: KeyboardEvent) {
  if (event.key === 'Escape') emit('close')
}

onMounted(() => {
  document.addEventListener('keydown', onKeydown)
  panel.value?.querySelector<HTMLElement>('button')?.focus()
})
onBeforeUnmount(() => document.removeEventListener('keydown', onKeydown))
</script>

<template>
  <Teleport to="body">
    <div
      ref="panel"
      role="alertdialog"
      aria-labelledby="doorway-refusal-title"
      class="pop fixed bottom-6 left-1/2 z-50 flex w-[min(420px,calc(100%-32px))] -translate-x-1/2 flex-col gap-3 rounded-[20px] border border-line-soft bg-panel p-4 shadow-[0_18px_40px_rgb(17_24_39/0.18)]"
    >
      <p id="doorway-refusal-title" class="font-semibold">🚪 {{ refusal.message }}</p>
      <ul
        v-if="refusal.unmet.length"
        class="flex flex-col gap-1 text-sm text-[#374151]"
        aria-label="Missing"
      >
        <li v-for="u in refusal.unmet" :key="u">• Add {{ u }}</li>
      </ul>
      <p v-if="refusal.rule" class="text-xs text-muted">Set by the doorway “{{ refusal.rule }}”.</p>
      <div class="flex flex-wrap gap-2">
        <button
          type="button"
          class="press h-9 rounded-full bg-accent px-4 text-sm font-semibold text-white"
          @click="emit('openCard')"
        >
          Open card
        </button>
        <button
          v-if="canOverride"
          type="button"
          class="ghost h-9 rounded-full border border-line px-4 text-sm font-semibold"
          @click="emit('moveAnyway')"
        >
          Move anyway
        </button>
        <button
          type="button"
          class="ghost ml-auto h-9 rounded-full px-3 text-sm font-semibold"
          @click="emit('close')"
        >
          Dismiss
        </button>
      </div>
    </div>
  </Teleport>
</template>
