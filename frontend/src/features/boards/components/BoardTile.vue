<script setup lang="ts">
import { RouterLink } from 'vue-router'

import type { BoardSummary } from '../api'

defineProps<{ board: BoardSummary; index: number }>()
</script>

<template>
  <RouterLink
    :to="{ name: 'board', params: { boardId: board.id } }"
    class="lift fade-up flex flex-col gap-3 rounded-2xl border border-line-soft bg-panel p-5 text-ink no-underline"
    :style="{ '--i': index }"
  >
    <span class="text-2xl" aria-hidden="true">{{ board.emoji ?? '📋' }}</span>
    <span class="font-semibold">{{ board.name }}</span>
    <span class="text-xs text-muted">{{ board.role }}</span>
  </RouterLink>
</template>
