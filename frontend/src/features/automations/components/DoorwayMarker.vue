<script setup lang="ts">
import { ref, useId } from 'vue'

import type { Automation } from '../api'
import { describe, type BoardRefs } from '../catalog'

defineProps<{
  doorways: Automation[]
  refs: BoardRefs
  listName: string
  /** Cards in the list that miss a warn-mode doorway's requirements, with what they miss. */
  warnings?: { title: string; unmet: string[] }[]
}>()

const open = ref(false)
const id = useId()
</script>

<template>
  <span class="relative" @mouseenter="open = true" @mouseleave="open = false">
    <button
      type="button"
      class="ghost flex h-[26px] items-center rounded-lg px-1 text-sm"
      :aria-expanded="open"
      :aria-controls="id"
      :aria-label="`${listName} doorways`"
      @click.stop="open = !open"
      @keydown.escape="open = false"
    >
      🚪<span
        v-if="warnings?.length"
        class="ml-0.5 rounded-full bg-warn-soft px-1.5 text-[11px] font-semibold text-warn"
        :aria-label="`${warnings.length} cards miss requirements`"
        >⚠ {{ warnings.length }}</span
      ><span v-else-if="doorways.length > 1" class="text-[11px] font-semibold text-muted">{{
        doorways.length
      }}</span>
    </button>
    <span
      v-if="open"
      :id="id"
      role="tooltip"
      class="pop absolute top-8 left-0 z-30 flex w-64 flex-col gap-1.5 rounded-[var(--radius-control)] border border-line-soft bg-panel p-3 text-[13px] font-normal shadow-pop"
    >
      <span v-for="w in warnings ?? []" :key="w.title" class="text-warn">
        ⚠ <strong class="font-semibold">{{ w.title }}</strong> needs {{ w.unmet.join(', ') }}
      </span>
      <span v-for="d in doorways" :key="d.id">
        <strong class="font-semibold">{{ d.name }}:</strong> {{ describe(d, refs) }}
      </span>
    </span>
  </span>
</template>
