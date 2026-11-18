<script setup lang="ts">
import { RouterLink } from 'vue-router'

import type { BoardSummary } from '../api'

defineProps<{ board: BoardSummary; index: number }>()
const emit = defineEmits<{ star: [] }>()
</script>

<template>
  <div
    class="lift fade-up relative flex flex-col overflow-hidden rounded-2xl border border-line-soft bg-panel"
    :style="{ '--i': index }"
  >
    <span class="h-1.5 w-full" :style="{ backgroundColor: board.color }" aria-hidden="true" />
    <RouterLink
      :to="{ name: 'board', params: { boardId: board.id } }"
      class="flex flex-1 flex-col gap-2 p-5 pr-12 text-ink no-underline"
    >
      <span class="text-2xl" aria-hidden="true">{{ board.emoji ?? '📋' }}</span>
      <span class="font-semibold">{{ board.name }}</span>
      <span v-if="board.description" class="line-clamp-2 text-sm text-muted">{{
        board.description
      }}</span>
      <span class="mt-auto text-xs text-muted">
        {{ board.role }} · {{ board.memberCount }}
        {{ board.memberCount === 1 ? 'member' : 'members' }}
      </span>
    </RouterLink>
    <button
      v-if="!board.archivedAt"
      type="button"
      class="ghost absolute top-4 right-3 flex h-9 w-9 items-center justify-center rounded-full text-lg"
      :aria-pressed="board.isStarred"
      :aria-label="board.isStarred ? `Unstar ${board.name}` : `Star ${board.name}`"
      @click="emit('star')"
    >
      <span :class="board.isStarred ? 'pop text-amber-500' : 'text-muted'" aria-hidden="true">{{
        board.isStarred ? '★' : '☆'
      }}</span>
    </button>
  </div>
</template>
