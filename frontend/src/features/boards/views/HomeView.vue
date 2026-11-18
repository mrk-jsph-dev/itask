<script setup lang="ts">
import { onMounted, ref } from 'vue'

import SkeletonBlock from '@/shared/ui/SkeletonBlock.vue'
import BoardTile from '../components/BoardTile.vue'
import { boardsApi, type BoardSummary } from '../api'

const boards = ref<BoardSummary[] | null>(null)

onMounted(async () => {
  boards.value = await boardsApi.mine()
})
</script>

<template>
  <section class="flex flex-col gap-6">
    <h1 class="text-2xl font-bold tracking-tight">Your boards</h1>
    <!-- Columns wrap to fit the screen: no horizontal scrolling. -->
    <div class="grid grid-cols-[repeat(auto-fill,minmax(230px,1fr))] gap-4">
      <template v-if="boards === null">
        <SkeletonBlock v-for="n in 4" :key="n" height="120px" />
      </template>
      <BoardTile v-for="(board, i) in boards ?? []" :key="board.id" :board="board" :index="i" />
    </div>
    <p v-if="boards?.length === 0" class="text-muted">No boards yet.</p>
  </section>
</template>
