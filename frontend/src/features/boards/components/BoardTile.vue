<script setup lang="ts">
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

import type { BoardSummary } from '../api'

const props = defineProps<{ board: BoardSummary; index: number }>()
const emit = defineEmits<{ star: [] }>()

const meta = computed(
  () =>
    `${props.board.role} · ${props.board.memberCount === 1 ? 'just you' : `${props.board.memberCount} members`}`,
)
</script>

<template>
  <div
    class="lift fade-up relative flex flex-col rounded-[18px] border border-line-soft bg-[#F6F8FB]"
    :style="{ '--i': Math.min(index, 8) }"
  >
    <RouterLink
      :to="{ name: 'board', params: { boardId: board.id } }"
      class="flex flex-1 flex-col gap-3 p-4 pr-12 text-ink no-underline"
    >
      <span class="flex items-center gap-2 text-[15px] font-semibold">
        <span class="text-[22px]" aria-hidden="true">{{ board.emoji ?? '📋' }}</span>
        <span class="min-w-0 break-words">{{ board.name }}</span>
      </span>
      <span v-if="board.description" class="line-clamp-1 text-[13px] text-muted">{{
        board.description
      }}</span>
      <span class="mt-auto text-[13px] text-muted">{{ meta }}</span>
      <span
        class="block h-1.5 rounded-full"
        :style="{ backgroundColor: board.color }"
        aria-hidden="true"
      />
    </RouterLink>
    <button
      v-if="!board.archivedAt"
      type="button"
      class="ghost absolute top-3 right-2.5 flex h-9 w-9 items-center justify-center rounded-full text-lg"
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
