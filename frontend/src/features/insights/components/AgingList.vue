<script setup lang="ts">
import { RouterLink } from 'vue-router'

import type { AgingCard } from '../api'
import { formatDays } from '../chart'

/** Open cards that have sat longest in their list; each row opens the card sheet. */
defineProps<{ boardId: string; cards: AgingCard[] }>()
</script>

<template>
  <p v-if="!cards.length" class="py-4 text-sm text-muted">No open cards waiting. 🎉</p>
  <ol v-else class="flex flex-col" data-testid="aging-list">
    <li
      v-for="(c, i) in cards"
      :key="c.id"
      class="fade-up border-t border-line-soft first:border-t-0"
      :style="{ '--i': Math.min(i, 8) }"
    >
      <RouterLink
        :to="{ name: 'card', params: { boardId, cardId: c.id } }"
        class="ghost -mx-2 flex items-center gap-3 rounded-[12px] px-2 py-2.5 text-ink no-underline"
        :aria-label="`${c.key} ${c.title}, ${formatDays(c.days)} in ${c.listName}`"
      >
        <span class="min-w-0 flex-1">
          <span class="block truncate text-sm font-medium">{{ c.title }}</span>
          <span class="block truncate text-xs text-muted">{{ c.key }} · {{ c.listName }}</span>
        </span>
        <span
          class="shrink-0 rounded-full bg-hover px-2.5 py-1 text-xs font-semibold tabular-nums"
          >{{ formatDays(c.days) }}</span
        >
      </RouterLink>
    </li>
  </ol>
</template>
