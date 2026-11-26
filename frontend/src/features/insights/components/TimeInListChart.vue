<script setup lang="ts">
import { computed } from 'vue'

import type { ListWip } from '../api'
import { formatDays, type ListBar } from '../chart'

/** Median days per list as horizontal bars, each row with its open count against the WIP limit. */
const props = defineProps<{ bars: ListBar[]; wip: ListWip[] }>()

const wipById = computed(() => new Map(props.wip.map((w) => [w.listId, w])))
const over = (w: ListWip | undefined) => !!w && w.limit !== null && w.open > w.limit

/** "3 open", "3 open of 5 WIP" or "6 open of 5 WIP · over the limit". */
function wipText(w: ListWip) {
  if (w.limit === null) return `${w.open} open`
  return `${w.open} open of ${w.limit} WIP${over(w) ? ' · over the limit' : ''}`
}
</script>

<template>
  <ul class="flex flex-col gap-3" data-testid="time-in-list">
    <li
      v-for="(b, i) in bars"
      :key="b.listId"
      class="fade-up grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-3 gap-y-1"
      :style="{ '--i': Math.min(i, 8) }"
    >
      <span class="truncate text-sm font-medium text-ink">
        <span aria-hidden="true">{{ b.emoji ?? '📋' }}</span> {{ b.name }}
      </span>
      <span class="text-sm tabular-nums text-ink">
        <span class="font-semibold">{{ formatDays(b.days) }}</span>
        <span class="sr-only"> median, </span>
        <span class="text-muted"> · {{ b.samples }} {{ b.samples === 1 ? 'stay' : 'stays' }}</span>
      </span>
      <svg class="col-span-2 block h-2.5 w-full" aria-hidden="true">
        <rect width="100%" height="10" rx="5" fill="var(--color-hover)" />
        <rect
          v-if="b.share > 0"
          :width="`${Math.max(b.share * 100, 1.5)}%`"
          height="10"
          rx="5"
          fill="var(--color-chart-done)"
        />
      </svg>
      <span
        v-if="wipById.get(b.listId)"
        class="col-span-2 text-xs"
        :class="over(wipById.get(b.listId)) ? 'font-semibold text-[#b45309]' : 'text-muted'"
      >
        <span v-if="over(wipById.get(b.listId))" aria-hidden="true">⚠️ </span>
        {{ wipText(wipById.get(b.listId)!) }}
      </span>
    </li>
  </ul>
</template>
